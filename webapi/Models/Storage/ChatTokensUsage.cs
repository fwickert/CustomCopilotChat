using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using CopilotChat.WebApi.Storage;

namespace CopilotChat.WebApi.Models.Storage;

/// <summary>
/// Information about usage tokens.
/// </summary>
public class ChatTokensUsage : IStorageEntity
{
    /// <summary>
    /// Id of the message.
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// Id of the user who sent this message.
    /// </summary>
    public string UserId { get; set; }

    /// <summary>
    /// Id of the chat this message belongs to.
    /// </summary>
    public string ChatId { get; set; }

    /// <summary>
    /// Timestamp of the message.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Counts of total token usage used to generate bot response.
    /// </summary>
    public Dictionary<string, int>? TokenUsage { get; set; }

    /// <summary>
    /// The partition key for the source.
    /// </summary>
    [JsonIgnore]
    public string Partition => this.ChatId;

    public ChatTokensUsage(string id, string userId, string chatId, DateTimeOffset timestamp, Dictionary<string, int>? tokenUsage)
    {
        this.Id = id;
        this.UserId = userId;
        this.ChatId = chatId;
        this.Timestamp = timestamp;
        this.TokenUsage = tokenUsage;
    }
}
