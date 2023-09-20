﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CopilotChat.WebApi.Auth;
using CopilotChat.WebApi.Hubs;
using CopilotChat.WebApi.Models.Request;
using CopilotChat.WebApi.Models.Response;
using CopilotChat.WebApi.Models.Storage;
using CopilotChat.WebApi.Options;
using CopilotChat.WebApi.Services;
using CopilotChat.WebApi.Skills;
using CopilotChat.WebApi.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace CopilotChat.WebApi.Controllers;

/// <summary>
/// Controller for importing documents.
/// </summary>
[ApiController]
public class DocumentImportController : ControllerBase
{
    /// <summary>
    /// Supported file types for import.
    /// </summary>
    private enum SupportedFileType
    {
        /// <summary>
        /// .txt
        /// </summary>
        Txt,

        /// <summary>
        /// .pdf
        /// </summary>
        Pdf,

        /// <summary>
        /// .md
        /// </summary>
        Md,

        /// <summary>
        /// .jpg
        /// </summary>
        Jpg,

        /// <summary>
        /// .png
        /// </summary>
        Png,

        /// <summary>
        /// .tif or .tiff
        /// </summary>
        Tiff
    };

    private readonly ILogger<DocumentImportController> _logger;
    private readonly DocumentMemoryOptions _options;
    private readonly OcrSupportOptions _ocrSupportOptions;
    private readonly ChatSessionRepository _sessionRepository;
    private readonly ChatMemorySourceRepository _sourceRepository;
    private readonly ChatMessageRepository _messageRepository;
    private readonly ChatParticipantRepository _participantRepository;
    private const string GlobalDocumentUploadedClientCall = "GlobalDocumentUploaded";
    private const string ReceiveMessageClientCall = "ReceiveMessage";
    private readonly IOcrEngine _ocrEngine;
    private readonly IAuthInfo _authInfo;
    private readonly IContentSafetyService? _contentSafetyService;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentImportController"/> class.
    /// </summary>
    public DocumentImportController(
        ILogger<DocumentImportController> logger,
        IOptions<DocumentMemoryOptions> documentMemoryOptions,
        IOptions<OcrSupportOptions> ocrSupportOptions,
        ChatSessionRepository sessionRepository,
        ChatMemorySourceRepository sourceRepository,
        ChatMessageRepository messageRepository,
        ChatParticipantRepository participantRepository,
        IOcrEngine ocrEngine,
        IAuthInfo authInfo,
        IContentSafetyService? contentSafety = null)
    {
        this._logger = logger;
        this._options = documentMemoryOptions.Value;
        this._ocrSupportOptions = ocrSupportOptions.Value;
        this._sessionRepository = sessionRepository;
        this._sourceRepository = sourceRepository;
        this._messageRepository = messageRepository;
        this._participantRepository = participantRepository;
        this._ocrEngine = ocrEngine;
        this._authInfo = authInfo;
        this._contentSafetyService = contentSafety;
    }

    /// <summary>
    /// Gets the status of content safety.
    /// </summary>
    /// <returns></returns>
    [HttpGet]
    [Route("contentSafety/status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public bool ContentSafetyStatus()
    {
        return this._contentSafetyService?.ContentSafetyStatus(this._logger) ?? false;
    }

    /// <summary>
    /// Service API for importing a document.
    /// </summary>
    [Route("importDocuments")]
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ImportDocumentsAsync(
        [FromServices] IKernel kernel,
        [FromServices] IHubContext<MessageRelayHub> messageRelayHubContext,
        [FromForm] DocumentImportForm documentImportForm)
    {
        try
        {
            await this.ValidateDocumentImportFormAsync(documentImportForm);
        }
        catch (ArgumentException ex)
        {
            return this.BadRequest(ex.Message);
        }

        this._logger.LogInformation("Importing {0} document(s)...", documentImportForm.FormFiles.Count());

        DocumentMessageContent documentMessageContent = new();
        IEnumerable<ImportResult> importResults = new List<ImportResult>();
        await Task.WhenAll(documentImportForm.FormFiles.Select(formFile =>
            this.ImportDocumentHelperAsync(kernel, formFile, documentImportForm).ContinueWith(task =>
                {
                    var importResult = task.Result;
                    if (importResult != null)
                    {
                        documentMessageContent.AddDocument(
                            formFile.FileName,
                            this.GetReadableByteString(formFile.Length),
                            importResult.IsSuccessful);
                        importResults = importResults.Append(importResult);
                    }
                }, TaskScheduler.Default)));

        // Broadcast the document uploaded event to other users.
        if (documentImportForm.DocumentScope == DocumentImportForm.DocumentScopes.Chat)
        {
            var chatMessage = await this.TryCreateDocumentUploadMessage(
                documentMessageContent,
                documentImportForm);
            if (chatMessage == null)
            {
                foreach (var importResult in importResults)
                {
                    await this.RemoveMemoriesAsync(kernel, importResult);
                }
                return this.BadRequest("Failed to create chat message. All documents are removed.");
            }

            var chatId = documentImportForm.ChatId.ToString();
            var userId = this._authInfo.UserId;
            await messageRelayHubContext.Clients.Group(chatId)
                .SendAsync(ReceiveMessageClientCall, chatId, userId, chatMessage);

            return this.Ok(chatMessage);
        }

        await messageRelayHubContext.Clients.All.SendAsync(
            GlobalDocumentUploadedClientCall,
            documentMessageContent.ToFormattedStringNamesOnly(),
            this._authInfo.Name
        );

        return this.Ok("Documents imported successfully to global scope.");
    }

    #region Private

    /// <summary>
    /// A class to store a document import results.
    /// </summary>
    private sealed class ImportResult
    {
        /// <summary>
        /// A boolean indicating whether the import is successful.
        /// </summary>
        public bool IsSuccessful => this.Keys.Any();

        /// <summary>
        /// The name of the collection that the document is inserted to.
        /// </summary>
        public string CollectionName { get; set; }

        /// <summary>
        /// The keys of the inserted document chunks.
        /// </summary>
        public IEnumerable<string> Keys { get; set; } = new List<string>();

        /// <summary>
        /// The number of tokens in the document.
        /// </summary>
        public long Tokens { get; set; } = 0;

        /// <summary>
        /// Create a new instance of the <see cref="ImportResult"/> class.
        /// </summary>
        /// <param name="collectionName">The name of the collection that the document is inserted to.</param>
        public ImportResult(string collectionName)
        {
            this.CollectionName = collectionName;
        }

        /// <summary>
        /// Create a new instance of the <see cref="ImportResult"/> class representing a failed import.
        /// </summary>
        public static ImportResult Fail() => new(string.Empty);

        /// <summary>
        /// Add a key to the list of keys.
        /// </summary>
        /// <param name="key">The key to be added.</param>
        public void AddKey(string key)
        {
            this.Keys = this.Keys.Append(key);
        }
    }

    /// <summary>
    /// Validates the document import form.
    /// </summary>
    /// <param name="documentImportForm">The document import form.</param>
    /// <returns></returns>
    /// <exception cref="ArgumentException">Throws ArgumentException if validation fails.</exception>
    private async Task ValidateDocumentImportFormAsync(DocumentImportForm documentImportForm)
    {
        // Make sure the user has access to the chat session if the document is uploaded to a chat session.
        if (documentImportForm.DocumentScope == DocumentImportForm.DocumentScopes.Chat
                && !(await this.UserHasAccessToChatAsync(this._authInfo.UserId, documentImportForm.ChatId)))
        {
            throw new ArgumentException("User does not have access to the chat session.");
        }

        var formFiles = documentImportForm.FormFiles;

        if (!formFiles.Any())
        {
            throw new ArgumentException("No files were uploaded.");
        }
        else if (formFiles.Count() > this._options.FileCountLimit)
        {
            throw new ArgumentException($"Too many files uploaded. Max file count is {this._options.FileCountLimit}.");
        }

        // Loop through the uploaded files and validate them before importing.
        foreach (var formFile in formFiles)
        {
            if (formFile.Length == 0)
            {
                throw new ArgumentException($"File {formFile.FileName} is empty.");
            }

            if (formFile.Length > this._options.FileSizeLimit)
            {
                throw new ArgumentException($"File {formFile.FileName} size exceeds the limit.");
            }

            // Make sure the file type is supported and validate any images if ContentSafety is enabled.
            var fileType = this.GetFileType(Path.GetFileName(formFile.FileName));
            switch (fileType)
            {
                case SupportedFileType.Txt:
                case SupportedFileType.Pdf:
                case SupportedFileType.Md:
                    break;
                case SupportedFileType.Jpg:
                case SupportedFileType.Png:
                case SupportedFileType.Tiff:
                    if (this._ocrSupportOptions.Type != OcrSupportOptions.OcrSupportType.None)
                    {
                        if (documentImportForm.UseContentSafety)
                        {
                            if (this._contentSafetyService == null || !this._contentSafetyService.ContentSafetyStatus(this._logger))
                            {
                                throw new ArgumentException("Unable to analyze image. Content Safety is currently disabled in the backend.");
                            }

                            var violations = new List<string>();
                            try
                            {
                                // Call the content safety controller to analyze the image
                                var imageAnalysisResponse = await this._contentSafetyService.ImageAnalysisAsync(formFile, default);
                                violations = this._contentSafetyService.ParseViolatedCategories(imageAnalysisResponse, this._contentSafetyService.Options.ViolationThreshold);
                            }
                            catch (Exception ex) when (!ex.IsCriticalException())
                            {
                                this._logger.LogError(ex, "Failed to analyze image {0} with Content Safety. Details: {{1}}", formFile.FileName, ex.Message);
                                throw new AggregateException($"Failed to analyze image {formFile.FileName} with Content Safety.", ex);
                            }

                            if (violations.Count > 0)
                            {
                                throw new ArgumentException($"Unable to upload image {formFile.FileName}. Detected undesirable content with potential risk: {string.Join(", ", violations)}");
                            }
                        }
                        break;
                    }
                    throw new ArgumentException($"Unsupported image file type: {fileType} when " +
                        $"{OcrSupportOptions.PropertyName}:{nameof(OcrSupportOptions.Type)} is set to " +
                        nameof(OcrSupportOptions.OcrSupportType.None));
                default:
                    throw new ArgumentException($"Unsupported file type: {fileType}");
            }
        }
    }

    /// <summary>
    /// Import a single document.
    /// </summary>
    /// <param name="kernel">The kernel.</param>
    /// <param name="formFile">The form file.</param>
    /// <param name="documentImportForm">The document import form.</param>
    /// <returns>Import result.</returns>
    private async Task<ImportResult> ImportDocumentHelperAsync(
        IKernel kernel,
        IFormFile formFile,
        DocumentImportForm documentImportForm)
    {
        var fileType = this.GetFileType(Path.GetFileName(formFile.FileName));
        var documentContent = string.Empty;
        switch (fileType)
        {
            case SupportedFileType.Txt:
            case SupportedFileType.Md:
                documentContent = await this.ReadTxtFileAsync(formFile);
                break;
            case SupportedFileType.Pdf:
                documentContent = this.ReadPdfFile(formFile);
                break;
            case SupportedFileType.Jpg:
            case SupportedFileType.Png:
            case SupportedFileType.Tiff:
                documentContent = await this.ReadTextFromImageFileAsync(formFile);
                if (documentContent.Trim().Length == 0)
                {
                    throw new ArgumentException($"Image {{{formFile.FileName}}} does not contain text.");
                }
                break;
            default:
                // This should never happen. Validation should have already caught this.
                return ImportResult.Fail();
        }

        this._logger.LogInformation("Importing document {0}", formFile.FileName);

        // Create memory source
        var memorySource = this.CreateMemorySource(formFile, documentImportForm);

        // Parse document content to memory
        ImportResult importResult = ImportResult.Fail();
        try
        {
            importResult = await this.ParseDocumentContentToMemoryAsync(
                kernel,
                formFile.FileName,
                documentContent,
                documentImportForm,
                memorySource.Id
            );
        }
        catch (Exception ex) when (!ex.IsCriticalException())
        {
            this._logger.LogDebug(ex, "Failed to parse {0} document content to memory.", formFile.FileName);
            return ImportResult.Fail();
        }

        // Upsert memory source
        memorySource.Tokens = importResult.Tokens;
        if (!(await this.TryUpsertMemorySourceAsync(memorySource)))
        {
            this._logger.LogDebug("Failed to upsert memory source for file {0}.", formFile.FileName);
            await this.RemoveMemoriesAsync(kernel, importResult);
            return ImportResult.Fail();
        }

        return importResult;
    }

    /// <summary>
    /// Create a memory source.
    /// </summary>
    /// <param name="formFile">The file to be uploaded</param>
    /// <param name="documentImportForm">The document upload form that contains additional necessary info</param>
    /// <returns>A MemorySource object.</returns>
    private MemorySource CreateMemorySource(
        IFormFile formFile,
        DocumentImportForm documentImportForm)
    {
        var chatId = documentImportForm.ChatId.ToString();
        var userId = this._authInfo.UserId;

        return new MemorySource(
            chatId,
            formFile.FileName,
            userId,
            MemorySourceType.File,
            formFile.Length,
            null
        );
    }

    /// <summary>
    /// Try to upsert a memory source.
    /// </summary>
    /// <param name="memorySource">The memory source to be uploaded</param>
    /// <returns>True if upsert is successful. False otherwise.</returns>
    private async Task<bool> TryUpsertMemorySourceAsync(MemorySource memorySource)
    {
        try
        {
            await this._sourceRepository.UpsertAsync(memorySource);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>
    /// Try to create a chat message that represents document upload.
    /// </summary>
    /// <param name="chatId">The chat id</param>
    /// <param name="userName">The user id</param>
    /// <param name="documentMessageContent">The document message content</param>
    /// <param name="documentImportForm">The document upload form that contains additional necessary info</param>
    /// <returns>A ChatMessage object if successful, null otherwise</returns>
    private async Task<ChatMessage?> TryCreateDocumentUploadMessage(
        DocumentMessageContent documentMessageContent,
        DocumentImportForm documentImportForm)
    {
        var chatId = documentImportForm.ChatId.ToString();
        var userId = this._authInfo.UserId;
        var userName = this._authInfo.Name;

        var chatMessage = ChatMessage.CreateDocumentMessage(
            userId,
            userName,
            chatId,
            documentMessageContent
        );

        try
        {
            await this._messageRepository.CreateAsync(chatMessage);
            return chatMessage;
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// Converts a `long` byte count to a human-readable string.
    /// </summary>
    /// <param name="bytes">Byte count</param>
    /// <returns>Human-readable string of bytes</returns>
    private string GetReadableByteString(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int i;
        double dblsBytes = bytes;
        for (i = 0; i < sizes.Length && bytes >= 1024; i++, bytes /= 1024)
        {
            dblsBytes = bytes / 1024.0;
        }

        return string.Format(CultureInfo.InvariantCulture, "{0:0.#}{1}", dblsBytes, sizes[i]);
    }

    /// <summary>
    /// Get the file type from the file extension.
    /// </summary>
    /// <param name="fileName">Name of the file.</param>
    /// <returns>A SupportedFileType.</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    private SupportedFileType GetFileType(string fileName)
    {
        string extension = Path.GetExtension(fileName).ToUpperInvariant();
        return extension.ToUpperInvariant() switch
        {
            ".TXT" => SupportedFileType.Txt,
            ".MD" => SupportedFileType.Md,
            ".PDF" => SupportedFileType.Pdf,
            ".JPG" => SupportedFileType.Jpg,
            ".JPEG" => SupportedFileType.Jpg,
            ".PNG" => SupportedFileType.Png,
            ".TIF" => SupportedFileType.Tiff,
            ".TIFF" => SupportedFileType.Tiff,
            _ => throw new ArgumentOutOfRangeException($"Unsupported file type: {extension}"),
        };
    }

    /// <summary>
    /// Reads the text content from an image file.
    /// </summary>
    /// <param name="file">An IFormFile object.</param>
    /// <returns>A string of the content of the file.</returns>
    private async Task<string> ReadTextFromImageFileAsync(IFormFile file)
    {
        var textFromFile = await this._ocrEngine.ReadTextFromImageFileAsync(file);
        return textFromFile;
    }

    /// <summary>
    /// Read the content of a text file.
    /// </summary>
    /// <param name="file">An IFormFile object.</param>
    /// <returns>A string of the content of the file.</returns>
    private async Task<string> ReadTxtFileAsync(IFormFile file)
    {
        using var streamReader = new StreamReader(file.OpenReadStream());
        return await streamReader.ReadToEndAsync();
    }

    /// <summary>
    /// Read the content of a PDF file, ignoring images.
    /// </summary>
    /// <param name="file">An IFormFile object.</param>
    /// <returns>A string of the content of the file.</returns>
    private string ReadPdfFile(IFormFile file)
    {
        var fileContent = string.Empty;

        using var pdfDocument = PdfDocument.Open(file.OpenReadStream());
        foreach (var page in pdfDocument.GetPages())
        {
            var text = ContentOrderTextExtractor.GetText(page);
            fileContent += text;
        }

        return fileContent;
    }

    /// <summary>
    /// Parse the content of the document to memory.
    /// </summary>
    /// <param name="kernel">The kernel instance from the service</param>
    /// <param name="documentName">The name of the uploaded document</param>
    /// <param name="content">The file content read from the uploaded document</param>
    /// <param name="documentImportForm">The document upload form that contains additional necessary info</param>
    /// <param name="memorySourceId">The ID of the MemorySource that the document content is linked to</param>
    private async Task<ImportResult> ParseDocumentContentToMemoryAsync(
        IKernel kernel,
        string documentName,
        string content,
        DocumentImportForm documentImportForm,
        string memorySourceId)
    {
        var targetCollectionName = documentImportForm.DocumentScope == DocumentImportForm.DocumentScopes.Global
            ? this._options.GlobalDocumentCollectionName
            : this._options.ChatDocumentCollectionNamePrefix + documentImportForm.ChatId;
        var importResult = new ImportResult(targetCollectionName);

        // Split the document into lines of text and then combine them into paragraphs.
        // Note that this is only one of many strategies to chunk documents. Feel free to experiment with other strategies.
        var lines = TextChunker.SplitPlainTextLines(content, this._options.DocumentLineSplitMaxTokens);
        var paragraphs = TextChunker.SplitPlainTextParagraphs(lines, this._options.DocumentChunkMaxTokens, this._options.DocumentLineSplitMaxTokens);

        // TODO: Perform the save in parallel.
        for (var i = 0; i < paragraphs.Count; i++)
        {
            var paragraph = paragraphs[i];
            var key = $"{memorySourceId}-{i}";
            await kernel.Memory.SaveInformationAsync(
                collection: targetCollectionName,
                text: paragraph,
                id: key,
                description: $"Document: {documentName}");
            importResult.AddKey(key);
            importResult.Tokens += TokenUtilities.TokenCount(paragraph);
        }

        this._logger.LogInformation(
            "Parsed {0} paragraphs from local file {1}",
            paragraphs.Count,
            documentName
        );

        return importResult;
    }

    /// <summary>
    /// Check if the user has access to the chat session.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="chatId">The chat session ID.</param>
    /// <returns>A boolean indicating whether the user has access to the chat session.</returns>
    private async Task<bool> UserHasAccessToChatAsync(string userId, Guid chatId)
    {
        return await this._participantRepository.IsUserInChatAsync(userId, chatId.ToString());
    }

    /// <summary>
    /// Remove the memories that were created during the import process if subsequent steps fail.
    /// </summary>
    /// <param name="kernel">The kernel instance from the service</param>
    /// <param name="importResult">The import result that contains the keys of the memories to be removed</param>
    /// <returns></returns>
    private async Task RemoveMemoriesAsync(IKernel kernel, ImportResult importResult)
    {
        foreach (var key in importResult.Keys)
        {
            try
            {
                await kernel.Memory.RemoveAsync(importResult.CollectionName, key);
            }
            catch (Exception ex) when (!ex.IsCriticalException())
            {
                this._logger.LogError(ex, "Failed to remove memory {0} from collection {1}. Skipped.", key, importResult.CollectionName);
                continue;
            }
        }
    }

    #endregion
}
