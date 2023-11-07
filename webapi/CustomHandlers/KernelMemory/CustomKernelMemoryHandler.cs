// Copyright (c) Microsoft. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory.Diagnostics;
using Microsoft.KernelMemory.Pipeline;
namespace CopilotChat.WebApi.CustomHandlers.KernelMemory;

public class CustomKernelMemoryHandler : IPipelineStepHandler, IHostedService
{
    private readonly IPipelineOrchestrator _orchestrator;
    private readonly ILogger<CustomKernelMemoryHandler> _log;

    public string StepName { get; }

    public CustomKernelMemoryHandler(string stepName,
               IPipelineOrchestrator orchestrator,
                      ILogger<CustomKernelMemoryHandler> log)
    {
        this.StepName = stepName;
        this._orchestrator = orchestrator;
        this._log = log ?? DefaultLogger<CustomKernelMemoryHandler>.Instance;
    }


    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        this._log.LogInformation("Starting {0}...", this.GetType().FullName);
        return this._orchestrator.AddHandlerAsync(this, cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        this._log.LogInformation("Stopping {0}...", this.GetType().FullName);
        return this._orchestrator.StopAllPipelinesAsync();
    }

    public async Task<(bool success, DataPipeline updatedPipeline)> InvokeAsync(DataPipeline pipeline, CancellationToken cancellationToken = default)
    {
        /* ... your custom ...
         * ... handler ...
         * ... business logic ... */
        // Remove this - here only to avoid build errors
        await Task.Delay(0, cancellationToken).ConfigureAwait(false);
        return (true, pipeline);
    }
}
