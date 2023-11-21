﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CopilotChat.WebApi.Models.Response;
using CopilotChat.WebApi.Options;
using CopilotChat.WebApi.Plugins.OpenApi.GitHubPlugin.Model;
using CopilotChat.WebApi.Plugins.OpenApi.JiraPlugin.Model;
using CopilotChat.WebApi.Plugins.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.SemanticKernel.Orchestration;
using Microsoft.SemanticKernel.Planners;
using Microsoft.SemanticKernel.Planning;
using Microsoft.SemanticKernel.TemplateEngine;
using Microsoft.SemanticKernel.TemplateEngine.Basic;

namespace CopilotChat.WebApi.Plugins.Chat;

/// <summary>
/// This plugin provides the functions to acquire external information.
/// </summary>
public class ExternalInformationPlugin
{
    /// <summary>
    /// High level logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// Prompt settings.
    /// </summary>
    private readonly PromptsOptions _promptOptions;

    /// <summary>
    /// Chat Copilot's planner to gather additional information for the chat context.
    /// </summary>
    private readonly CopilotChatPlanner _planner;

    /// <summary>
    ///  Options for the planner.
    /// </summary>
    public PlannerOptions? PlannerOptions { get; }

    /// <summary>
    /// Proposed plan to return for approval.
    /// </summary>
    public ProposedPlan? ProposedPlan { get; private set; }

    /// <summary>
    /// Stepwise thought process to return for view.
    /// </summary>
    public PlanExecutionMetadata? StepwiseThoughtProcess { get; private set; }

    /// <summary>
    /// Header to indicate plan results.
    /// </summary>
    private const string ResultHeader = "RESULT: ";

    /// <summary>
    /// Create a new instance of ExternalInformationPlugin.
    /// </summary>
    public ExternalInformationPlugin(
        IOptions<PromptsOptions> promptOptions,
        CopilotChatPlanner planner,
        ILogger logger)
    {
        this._promptOptions = promptOptions.Value;
        this._planner = planner;
        this.PlannerOptions = planner.PlannerOptions;
        this._logger = logger;
    }

    public string FormattedFunctionsString(Plan plan) { return string.Join("; ", this.GetPlanSteps(plan)); }

    /// <summary>
    /// Invoke planner to generate a new plan or extract relevant additional knowledge.
    /// </summary>
    public async Task<string> InvokePlannerAsync(
        string userIntent,
        SKContext context,
        CancellationToken cancellationToken = default)
    {
        // TODO: [Issue #2106] Calculate planner and plan token usage
        var functions = this._planner.Kernel.Functions.GetFunctionViews();
        if (functions.IsNullOrEmpty())
        {
            return string.Empty;
        }

        var contextString = this.GetChatContextString(context);
        var goal = $"Given the following context, accomplish the user intent.\nContext:\n{contextString}\n{userIntent}";

        // Run stepwise planner if PlannerOptions.Type == Stepwise
        if (this._planner.PlannerOptions?.Type == PlanType.Stepwise)
        {
            return await this.RunStepwisePlannerAsync(goal, context, cancellationToken);
        }

        // Create a plan and set it in context for approval.
        Plan? plan = null;
        var plannerOptions = this._planner.PlannerOptions ?? new PlannerOptions(); // Use default planner options if planner options are null.
        int retriesAvail = plannerOptions.ErrorHandling.AllowRetries
            ? plannerOptions.ErrorHandling.MaxRetriesAllowed : 0;

        do
        { // TODO: [Issue #2256] Remove InvalidPlan retry logic once Core team stabilizes planner
            try
            {
                plan = await this._planner.CreatePlanAsync(goal, this._logger, cancellationToken);
            }
            catch (Exception e)
            {
                if (--retriesAvail >= 0)
                {
                    this._logger.LogWarning("Retrying CreatePlan on error: {0}", e.Message);
                    continue;
                }
                throw;
            }
        } while (plan == null);

        if (plan.Steps.Count > 0)
        {
            // Merge any variables from ask context into plan parameters as these will be used on plan execution.
            // These context variables come from user input, so they are prioritized.
            if (plannerOptions.Type == PlanType.Action)
            {
                // Parameters stored in plan's top level state
                this.MergeContextIntoPlan(context.Variables, plan.Parameters);
            }
            else
            {
                foreach (var step in plan.Steps)
                {
                    this.MergeContextIntoPlan(context.Variables, step.Parameters);
                }
            }

            this.ProposedPlan = new ProposedPlan(plan, plannerOptions.Type, PlanState.NoOp, userIntent, context.Variables.Input);
        }

        return string.Empty;
    }

    public async Task<string> ExecutePlanAsync(
        SKContext context,
        Plan plan,
        CancellationToken cancellationToken = default)
    {
        // Reload the plan with the planner's kernel so it has full context to be executed
        var newPlanContext = this._planner.Kernel.CreateNewContext(context.Variables, this._planner.Kernel.Functions, this._planner.Kernel.LoggerFactory);
        string planJson = JsonSerializer.Serialize(plan);
        plan = Plan.FromJson(planJson, this._planner.Kernel.Functions);

        // Invoke plan
        var functionResult = await plan.InvokeAsync(newPlanContext, null, cancellationToken);
        var functionsUsed = $"FUNCTIONS USED: {this.FormattedFunctionsString(plan)}";

        // TODO: #2581 Account for planner system instructions
        int tokenLimit = int.Parse(context.Variables["tokenLimit"], new NumberFormatInfo())
            - TokenUtils.TokenCount(functionsUsed)
            - TokenUtils.TokenCount(ResultHeader);

        // The result of the plan may be from an OpenAPI plugin. Attempt to extract JSON from the response.
        bool extractJsonFromOpenApi =
            this.TryExtractJsonFromOpenApiPlanResult(newPlanContext, newPlanContext.Result, out string planResult);
        if (extractJsonFromOpenApi)
        {
            planResult = this.OptimizeOpenApiPluginJson(planResult, tokenLimit, plan);
        }
        else
        {
            // If not, use result of plan execution directly.
            planResult = newPlanContext.Variables.Input;
        }

        return $"{functionsUsed}\n{ResultHeader}{planResult.Trim()}";
    }

    /// <summary>
    /// Determines whether to use the stepwise planner result as the bot response, thereby bypassing meta prompt generation and completion.
    /// </summary>
    /// <param name="planResult">The result obtained from the stepwise planner.</param>
    /// <returns>
    /// True if the stepwise planner result should be used as the bot response,
    /// false otherwise.
    /// </returns>
    /// <remarks>
    /// This method checks the following conditions:
    /// 1. The plan result is not null, empty, or whitespace.
    /// 2. The planner options are specified, and the plan type is set to Stepwise.
    /// 3. The UseStepwiseResultAsBotResponse option is enabled.
    /// 4. The StepwiseThoughtProcess is not null.
    /// </remarks>
    public bool UseStepwiseResultAsBotResponse(string planResult)
    {
        return !string.IsNullOrWhiteSpace(planResult)
            && this.PlannerOptions?.Type == PlanType.Stepwise
            && this.PlannerOptions.UseStepwiseResultAsBotResponse
            && this.StepwiseThoughtProcess != null;
    }

    /// <summary>
    /// Executes the stepwise planner with a given goal and context, and returns the result along with descriptive text.
    /// Also sets any metadata associated with stepwise planner execution.
    /// </summary>
    /// <param name="goal">The goal to be achieved by the stepwise planner.</param>
    /// <param name="context">The SKContext containing the necessary information for the planner.</param>
    /// <param name="cancellationToken">A CancellationToken to observe while waiting for the task to complete.</param>
    /// <returns>
    /// A formatted string containing the result of the stepwise planner and a supplementary message to guide the model in using the result.
    /// </returns>
    private async Task<string> RunStepwisePlannerAsync(string goal, SKContext context, CancellationToken cancellationToken)
    {
        var plannerContext = context.Clone();
        var functionResult = await this._planner.RunStepwisePlannerAsync(goal, context, cancellationToken);

        // Populate the execution metadata.
        var plannerResult = functionResult.GetValue<string>()?.Trim() ?? string.Empty;
        this.StepwiseThoughtProcess = new PlanExecutionMetadata(
            plannerContext.Variables["stepsTaken"],
            plannerContext.Variables["timeTaken"],
            plannerContext.Variables["pluginCount"],
            plannerResult);

        // Return empty string if result was not found so it's omitted from the meta prompt.
        if (plannerResult.Contains("Result not found, review 'stepsTaken' to see what happened.", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        // Parse the steps taken to determine which functions were used.
        if (plannerContext.Variables.TryGetValue("stepsTaken", out var stepsTaken))
        {
            var steps = JsonSerializer.Deserialize<List<SystemStep>>(stepsTaken);
            var functionsUsed = new HashSet<string>();
            steps?.ForEach(step =>
            {
                if (!step.Action.IsNullOrEmpty()) { functionsUsed.Add(step.Action); }
            });

            var planFunctions = string.Join(", ", functionsUsed);
            plannerContext.Variables.Set("planFunctions", functionsUsed.Count > 0 ? planFunctions : "N/A");
        }

        // Render the supplement to guide the model in using the result.
        var promptTemplateFactory = new BasicPromptTemplateFactory();
        var promptTemplate = promptTemplateFactory.Create(this._promptOptions.StepwisePlannerSupplement, new PromptTemplateConfig());
        var resultSupplement = await promptTemplate.RenderAsync(plannerContext, cancellationToken);

        return $"{resultSupplement}\n\nResult:\n\"{plannerResult}\"";
    }

    /// <summary>
    /// Merge any variables from context into plan parameters.
    /// </summary>
    private void MergeContextIntoPlan(ContextVariables variables, ContextVariables planParams)
    {
        foreach (var param in planParams)
        {
            if (param.Key.Equals("INPUT", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (variables.TryGetValue(param.Key, out string? value))
            {
                planParams.Set(param.Key, value);
            }
        }
    }

    /// <summary>
    /// Try to extract json from the planner response as if it were from an OpenAPI plugin.
    /// </summary>
    private bool TryExtractJsonFromOpenApiPlanResult(SKContext context, string openApiPluginResponse, out string json)
    {
        try
        {
            JsonNode? jsonNode = JsonNode.Parse(openApiPluginResponse);
            string contentType = jsonNode?["contentType"]?.ToString() ?? string.Empty;
            if (contentType.StartsWith("application/json", StringComparison.InvariantCultureIgnoreCase))
            {
                var content = jsonNode?["content"]?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(content))
                {
                    json = content;
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            this._logger.LogDebug("Unable to extract JSON from planner response, it is likely not from an OpenAPI plugin.");
        }
        catch (InvalidOperationException)
        {
            this._logger.LogDebug("Unable to extract JSON from planner response, it may already be proper JSON.");
        }

        json = string.Empty;

        return false;
    }

    /// <summary>
    /// Try to optimize json from the planner response
    /// based on token limit
    /// </summary>
    private string OptimizeOpenApiPluginJson(string jsonContent, int tokenLimit, Plan plan)
    {
        // Remove all new line characters + leading and trailing white space
        jsonContent = Regex.Replace(jsonContent.Trim(), @"[\n\r]", string.Empty);
        var document = JsonDocument.Parse(jsonContent);
        string lastPluginInvoked = plan.Steps[^1].PluginName;
        string lastFunctionInvoked = plan.Steps[^1].Name;
        bool trimResponse = false;

        // The json will be deserialized based on the response type of the particular operation that was last invoked by the planner
        // The response type can be a custom trimmed down json structure, which is useful in staying within the token limit
        Type responseType = this.GetOpenApiFunctionResponseType(ref document, ref lastPluginInvoked, ref lastFunctionInvoked, ref trimResponse);

        if (trimResponse)
        {
            // Deserializing limits the json content to only the fields defined in the respective OpenApi's Model classes
            var functionResponse = JsonSerializer.Deserialize(jsonContent, responseType);
            jsonContent = functionResponse != null ? JsonSerializer.Serialize(functionResponse) : string.Empty;
            document = JsonDocument.Parse(jsonContent);
        }

        int jsonContentTokenCount = TokenUtils.TokenCount(jsonContent);

        // Return the JSON content if it does not exceed the token limit
        if (jsonContentTokenCount < tokenLimit)
        {
            return jsonContent;
        }

        List<object> itemList = new();

        // Some APIs will return a JSON response with one property key representing an embedded answer.
        // Extract this value for further processing
        string resultsDescriptor = string.Empty;

        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            int propertyCount = 0;
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                propertyCount++;
            }

            if (propertyCount == 1)
            {
                // Save property name for result interpolation
                JsonProperty firstProperty = document.RootElement.EnumerateObject().First();
                tokenLimit -= TokenUtils.TokenCount(firstProperty.Name);
                resultsDescriptor = string.Format(CultureInfo.InvariantCulture, "{0}: ", firstProperty.Name);

                // Extract object to be truncated
                JsonElement value = firstProperty.Value;
                document = JsonDocument.Parse(value.GetRawText());
            }
        }

        // Detail Object
        // To stay within token limits, attempt to truncate the list of properties
        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                int propertyTokenCount = TokenUtils.TokenCount(property.ToString());

                if (tokenLimit - propertyTokenCount > 0)
                {
                    itemList.Add(property);
                    tokenLimit -= propertyTokenCount;
                }
                else
                {
                    break;
                }
            }
        }

        // Summary (List) Object
        // To stay within token limits, attempt to truncate the list of results
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in document.RootElement.EnumerateArray())
            {
                int itemTokenCount = TokenUtils.TokenCount(item.ToString());

                if (tokenLimit - itemTokenCount > 0)
                {
                    itemList.Add(item);
                    tokenLimit -= itemTokenCount;
                }
                else
                {
                    break;
                }
            }
        }

        return itemList.Count > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0}{1}", resultsDescriptor, JsonSerializer.Serialize(itemList))
            : string.Format(CultureInfo.InvariantCulture, "JSON response from {0} is too large to be consumed at this time.", this._planner.PlannerOptions?.Type == PlanType.Sequential ? "plan" : lastPluginInvoked);
    }

    private Type GetOpenApiFunctionResponseType(ref JsonDocument document, ref string lastPluginInvoked, ref string lastFunctionInvoked, ref bool trimResponse)
    {
        // TODO: [Issue #93] Find a way to determine response type if multiple steps are invoked
        Type responseType = typeof(object); // Use a reasonable default response type

        // Different operations under the plugin will return responses as json structures;
        // Prune each operation response according to the most important/contextual fields only to avoid going over the token limit
        // Check what the last function invoked was and deserialize the JSON content accordingly
        if (string.Equals(lastPluginInvoked, "GitHubPlugin", StringComparison.Ordinal))
        {
            trimResponse = true;
            responseType = this.GetGithubPluginResponseType(ref document);
        }
        else if (string.Equals(lastPluginInvoked, "JiraPlugin", StringComparison.Ordinal))
        {
            trimResponse = true;
            responseType = this.GetJiraPluginResponseType(ref document, ref lastFunctionInvoked);
        }

        return responseType;
    }

    private Type GetGithubPluginResponseType(ref JsonDocument document)
    {
        return document.RootElement.ValueKind == JsonValueKind.Array ? typeof(PullRequest[]) : typeof(PullRequest);
    }

    private Type GetJiraPluginResponseType(ref JsonDocument document, ref string lastFunctionInvoked)
    {
        if (lastFunctionInvoked == "GetIssue")
        {
            return document.RootElement.ValueKind == JsonValueKind.Array ? typeof(IssueResponse[]) : typeof(IssueResponse);
        }

        return typeof(IssueResponse);
    }

    /// <summary>
    /// Retrieves the steps in a plan that was executed successfully.
    /// </summary>
    /// <param name="plan">The plan object.</param>
    /// <returns>A list of strings representing the successfully executed steps in the plan.</returns>
    private List<string> GetPlanSteps(Plan plan)
    {
        List<string> steps = new();
        foreach (var step in plan.Steps)
        {
            steps.Add($"{step.PluginName}.{step.Name}");
        }

        return steps;
    }

    /// <summary>
    /// Returns a string representation of the chat context, excluding some variables that are only relevant to the ChatPlugin execution context and should be ignored by the planner.
    /// This helps clarify the context that is passed to the planner as well as save on tokens.
    /// </summary>
    /// <param name="context">The chat context object that contains the variables and their values.</param>
    /// <returns>A string with one line per variable, in the format "key: value", except for variables that contain "TokenUsage", "tokenLimit", or "chatId" in their names, which are skipped.</returns>
    private string GetChatContextString(SKContext context)
    {
        return string.Join("\n", context.Variables.Where(v => !(
            v.Key.Contains("TokenUsage", StringComparison.CurrentCultureIgnoreCase)
            || v.Key.Contains("tokenLimit", StringComparison.CurrentCultureIgnoreCase)
            || v.Key.Contains("chatId", StringComparison.CurrentCultureIgnoreCase)))
            .Select(v => $"{v.Key}: {v.Value}"));
    }
}
