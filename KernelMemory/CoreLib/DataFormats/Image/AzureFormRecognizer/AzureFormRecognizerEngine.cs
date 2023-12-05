// Copyright (c) Microsoft. All rights reserved.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.AI.DocumentIntelligence;
//using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory.Configuration;
using Microsoft.KernelMemory.Diagnostics;
using Microsoft.KernelMemory.FileSystem.DevTools;

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

        switch (config.Auth)
        {
            case AzureFormRecognizerConfig.AuthTypes.AzureIdentity:
                //this._recognizerClient = new DocumentAnalysisClient(new Uri(config.Endpoint), new DefaultAzureCredential());
                this._documentIntelligenceClient = new DocumentIntelligenceClient(new Uri(config.Endpoint), new DefaultAzureCredential());
                break;

            case AzureFormRecognizerConfig.AuthTypes.APIKey:
                if (string.IsNullOrEmpty(config.APIKey))
                {
                    this._log.LogCritical("Azure Form Recognizer API key is empty");
                    throw new ConfigurationException("Azure Form Recognizer API key is empty");
                }

                //this._recognizerClient = new DocumentAnalysisClient(new Uri(config.Endpoint), new AzureKeyCredential(config.APIKey));
                this._documentIntelligenceClient = new DocumentIntelligenceClient(new Uri(config.Endpoint), new AzureKeyCredential(config.APIKey));
                break;

            default:
                this._log.LogCritical("Azure Form Recognizer authentication type '{0}' undefined or not supported", config.Auth);
                throw new ConfigurationException($"Azure Form Recognizer authentication type '{config.Auth}' undefined or not supported");
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

        
        

        var operation = await this._documentIntelligenceClient.AnalyzeDocumentAsync
            (WaitUntil.Completed, "prebuilt-layout", content, null, null, null, null, null,
            ContentFormat.Markdown,cancellationToken: cancellationToken).ConfigureAwait(false);


        // Wait for the result
        Response<AnalyzeResult> operationResponse = await operation.WaitForCompletionAsync(cancellationToken).ConfigureAwait(false);

        //Le Value.content n'est pas structuré. Alors on va prendre les KeyValuesPairs + Paragraphs + Tables pour structurer
        // Exemple de structure : Informations de devis, Le devis, autres informations

        //StringBuilder stringBuilder = new StringBuilder();
        //stringBuilder.AppendLine("[START Informations de devis]");

        //foreach (AnalyzedDocument doc in operation.Value.Documents)
        //{
        //    foreach (KeyValuePair<string, DocumentField> field in doc.Fields)
        //    {
        //        switch (field.Value.FieldType)
        //        {
        //            case DocumentFieldType.List:
        //                foreach (DocumentField item in field.Value.Value.AsList())
        //                {
        //                    stringBuilder.AppendLine("Items: ");
        //                    if (item.FieldType == DocumentFieldType.Dictionary)
        //                    {
        //                        foreach (KeyValuePair<string, DocumentField> itemField in item.Value.AsDictionary())
        //                        {
        //                            if (itemField.Value.FieldType == DocumentFieldType.String)
        //                            {
        //                                stringBuilder.AppendLine($"{itemField.Key} : {itemField.Value.Content}");
        //                            }
        //                            if (itemField.Value.FieldType == DocumentFieldType.Currency)
        //                            {
        //                                CurrencyValue itemAmount = itemField.Value.Value.AsCurrency();
        //                                stringBuilder.AppendLine($"{itemField.Key} : {itemAmount.Symbol}{itemAmount.Amount}");
        //                            }
        //                        }
        //                    }
        //                    else
        //                    {
        //                        stringBuilder.AppendLine($"{item.Content}");
        //                    }
        //                }
        //                break;
        //            default:
        //                stringBuilder.AppendLine($"{field.Key} : {field.Value.Content}");
        //                break;
        //        }

        //    }

        //    stringBuilder.AppendLine("[END Informations de devis]");


        //    foreach (DocumentTable table in operation.Value.Tables)
        //    {
        //        stringBuilder.AppendLine("[START Table]");
        //        foreach (DocumentTableCell cell in table.Cells)
        //        {
        //            stringBuilder.AppendLine($"[C{cell.ColumnIndex}:R{cell.RowIndex}]={cell.Content}");
        //        }
        //        stringBuilder.AppendLine("[END Table]");
        //    }
        //}




        return operationResponse.Value.Content;
        //return stringBuilder.ToString(); ;
    }
}
