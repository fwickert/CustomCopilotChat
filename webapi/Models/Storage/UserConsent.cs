// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Text.Json.Serialization;
using CopilotChat.WebApi.Storage;

namespace CopilotChat.WebApi.Models.Storage;

public class UserConsent : IStorageEntity
{
    /// <summary>
    /// Id of the message. Put in the UserID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The partition key for the session.
    /// </summary>
    [JsonIgnore]
    public string Partition => this.Id;

    /// <summary>
    /// Timestamp of the chat creation.
    /// </summary>
    public DateTimeOffset ConsentOn { get; set; }

    /// <summary>
    /// Consent accepted or not
    /// </summary>
    public bool Consent { get; set; }

    /// <summary>
    /// Version  of consent
    /// </summary>
    ///
    public string ConsentVersion { get; set; } = string.Empty;
}
