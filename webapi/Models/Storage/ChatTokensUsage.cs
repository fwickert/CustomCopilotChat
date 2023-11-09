// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System;
using CopilotChat.WebApi.Storage;
using System.Text.Json.Serialization;

namespace CopilotChat.WebApi.Models.Storage;

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

    ///<summary>
    /// user Name
    /// </summary>
    public string UserName { get; set; }

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
    public IDictionary<string, int>? TokenUsage { get; set; }

    /// <summary>
    /// The partition key for the source.
    /// </summary>
    [JsonIgnore]
    public string Partition => this.ChatId;

    public ChatTokensUsage(string id, string userId, string userName, string chatId, DateTimeOffset timestamp, IDictionary<string, int>? tokenUsage)
    {
        this.Id = id;
        this.UserId = userId;
        this.UserName = userName;
        this.ChatId = chatId;
        this.Timestamp = timestamp;
        this.TokenUsage = tokenUsage;
    }
}
