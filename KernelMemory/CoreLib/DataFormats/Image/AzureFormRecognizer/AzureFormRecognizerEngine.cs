// Copyright (c) Microsoft. All rights reserved.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Core;

//using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory.Configuration;
using Microsoft.KernelMemory.Diagnostics;
using Microsoft.KernelMemory.FileSystem.DevTools;
using Microsoft.SemanticKernel.Diagnostics;

namespace Microsoft.KernelMemory.DataFormats.Image.AzureFormRecognizer;

/// <summary>
/// OCR engine based on Azure.AI.FormRecognizer.
/// </summary>
public class AzureFormRecognizerEngine : IOcrEngine
{
    //private readonly DocumentAnalysisClient _recognizerClient;
    private readonly DocumentIntelligenceClient _documentIntelligenceClient;
    private readonly ILogger<AzureFormRecognizerEngine> _log;

    /// <summary>
    /// Creates a new instance of the Azure Form Recognizer.
    /// </summary>
    /// <param name="config">The AzureFormRecognizerConfig config for this service</param>
    /// <param name="log">Application logger</param>
    public AzureFormRecognizerEngine(
        AzureFormRecognizerConfig config,
        ILogger<AzureFormRecognizerEngine>? log = null)
    {
        this._log = log ?? DefaultLogger<AzureFormRecognizerEngine>.Instance;

        AzureAIDocumentIntelligenceClientOptions options = new()
        {
            Retry =
            {
                Delay = TimeSpan.FromSeconds(2),
                MaxDelay = TimeSpan.FromSeconds(16),
                MaxRetries = 10,
                Mode = RetryMode.Fixed
            },
            Diagnostics = { IsLoggingContentEnabled = true }
        };

        switch (config.Auth)
        {
            case AzureFormRecognizerConfig.AuthTypes.AzureIdentity:
                //this._recognizerClient = new DocumentAnalysisClient(new Uri(config.Endpoint), new DefaultAzureCredential());
                this._documentIntelligenceClient = new DocumentIntelligenceClient(new Uri(config.Endpoint), new DefaultAzureCredential(), options);
                break;

            case AzureFormRecognizerConfig.AuthTypes.APIKey:
                if (string.IsNullOrEmpty(config.APIKey))
                {
                    this._log.LogCritical("Document Intelligent Service API key is empty");
                    throw new ConfigurationException("Document Intelligent Service API key is empty");
                }

                //this._recognizerClient = new DocumentAnalysisClient(new Uri(config.Endpoint), new AzureKeyCredential(config.APIKey));
                this._documentIntelligenceClient = new DocumentIntelligenceClient(new Uri(config.Endpoint), new AzureKeyCredential(config.APIKey), options);
                break;

            default:
                this._log.LogCritical("Document Intelligent Service authentication type '{0}' undefined or not supported", config.Auth);
                throw new ConfigurationException($"Document Intelligent Service authentication type '{config.Auth}' undefined or not supported");
        }

    }

    ///<inheritdoc/>
    public async Task<string> ExtractTextFromImageAsync(Stream imageContent, CancellationToken cancellationToken = default)
    {
        // Start the OCR operation
        //var operation = await this._recognizerClient.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-read", imageContent, cancellationToken: cancellationToken).ConfigureAwait(false);

        var content = new AnalyzeDocumentContent()
        {
            Base64Source = new BinaryData(imageContent.ReadAllBytes())
        };

        try
        {
            var operation = await this._documentIntelligenceClient.AnalyzeDocumentAsync
          (WaitUntil.Completed, "prebuilt-layout", content, null, null, null, null, null,
          ContentFormat.Markdown, cancellationToken: cancellationToken).ConfigureAwait(false);

            // Wait for the result
            Response<AnalyzeResult> operationResponse = await operation.WaitForCompletionAsync(cancellationToken).ConfigureAwait(false);

            return operationResponse.Value.Content;
        }
        catch (RequestFailedException e) when (e.Status == 429)
        {
            throw (new HttpOperationException("Document Intelligent Service is busy, please try again later", e));
        }
        catch (Exception e)
        {
            throw e;
        }
    }
}
