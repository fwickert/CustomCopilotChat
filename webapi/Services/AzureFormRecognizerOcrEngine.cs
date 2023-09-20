﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure;
using Azure.AI.FormRecognizer;
using Azure.AI.FormRecognizer.Models;
using Microsoft.AspNetCore.Http;

namespace CopilotChat.WebApi.Services;

/// <summary>
/// Wrapper for the Azure.AI.FormRecognizer. This allows Form Recognizer to be used as the OCR engine for reading text from files with an image MIME type.
/// </summary>
public class AzureFormRecognizerOcrEngine : IOcrEngine
{
    /// <summary>
    /// Creates a new instance of the AzureFormRecognizerOcrEngine passing in the Form Recognizer endpoint and key.
    /// </summary>
    /// <param name="endpoint">The endpoint for accessing a provisioned Azure Form Recognizer instance</param>
    /// <param name="credential">The AzureKeyCredential containing the provisioned Azure Form Recognizer access key</param>
    public AzureFormRecognizerOcrEngine(string endpoint, AzureKeyCredential credential)
    {
        this.FormRecognizerClient = new FormRecognizerClient(new Uri(endpoint), credential);
    }

    public FormRecognizerClient FormRecognizerClient { get; }

    ///<inheritdoc/>
    public async Task<string> ReadTextFromImageFileAsync(IFormFile imageFile)
    {
        await using (var imgStream = new MemoryStream())
        {
            await imageFile.CopyToAsync(imgStream);
            imgStream.Position = 0;

            // Start the OCR operation
            RecognizeContentOperation operation = await this.FormRecognizerClient.StartRecognizeContentAsync(imgStream);

            // Wait for the result
            Response<FormPageCollection> operationResponse = await operation.WaitForCompletionAsync();
            FormPageCollection formPages = operationResponse.Value;

            StringBuilder text = new();
            foreach (FormPage page in formPages)
            {
                foreach (FormLine line in page.Lines)
                {
                    string lineText = string.Join(" ", line.Words.Select(word => word.Text));
                    text.AppendLine(lineText);
                }
            }
            return text.ToString();
        }
    }
}
