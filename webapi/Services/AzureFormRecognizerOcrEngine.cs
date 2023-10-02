// Copyright (c) Microsoft. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure;
using Azure.AI.FormRecognizer;
using Azure.AI.FormRecognizer.DocumentAnalysis;
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


        this.DocumentAnalysisClient = new DocumentAnalysisClient(new Uri(endpoint), credential);
    }

    public FormRecognizerClient FormRecognizerClient { get; }

    public DocumentAnalysisClient DocumentAnalysisClient { get; }

    ///<inheritdoc/>
    public async Task<string> ReadTextFromImageFileAsync(IFormFile imageFile)
    {
        await using (var imgStream = new MemoryStream())
        {
            await imageFile.CopyToAsync(imgStream);
            imgStream.Position = 0;

            // Start the OCR operation

            //RecognizeContentOperation operation = await this.FormRecognizerClient.StartRecognizeContentAsync(imgStream);

            AnalyzeDocumentOperation operation = await this.DocumentAnalysisClient.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-read", imgStream);
            //AnalyzeDocumentOperation operation = await this.DocumentAnalysisClient.AnalyzeDocumentFromUriAsync(WaitUntil.Completed,"prebuilt-read",  new Uri("https://storageholaipourtous.blob.core.windows.net/test/DemoDocx.docx?sp=r&st=2023-10-02T13:44:28Z&se=2024-10-02T21:44:28Z&spr=https&sv=2022-11-02&sr=b&sig=YWV5i1OZocpUs2xAyWaA29juwMtFrdYeWg5znVhl0r4%3D"));

            // Wait for the result
            //Response<FormPageCollection> operationResponse = await operation.WaitForCompletionAsync();
            Response<AnalyzeResult> operationResponse = await operation.WaitForCompletionAsync();
            //FormPageCollection formPages = operationResponse.Value;
            AnalyzeResult formPages = operationResponse.Value;

            StringBuilder text = new();

            //pour chaque paragraphe

            foreach (var paragraph in formPages.Paragraphs)
            {
                text.AppendLine(paragraph.Content);
            }

            ////foreach (FormPage page in formPages)
            //foreach (DocumentPage page in formPages.Pages)
            //{
            //   //foreach (FormLine line in page.Lines)
            //    foreach (DocumentLine line in page.Lines)
            //    {
            //        //string lineText = string.Join(" ", line.Words.Select(word => word.Text));
            //        string lineText = string.Join(" ", line.GetWords().Select(word => word.Content));
            //        text.AppendLine(lineText);
            //    }
            //}
            return text.ToString();
        }
    }
}
