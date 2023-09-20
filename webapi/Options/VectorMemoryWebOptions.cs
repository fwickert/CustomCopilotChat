﻿// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel.DataAnnotations;

namespace CopilotChat.WebApi.Options;

/// <summary>
/// Configuration settings for connecting to a vector memory web service.
/// </summary>
public class VectorMemoryWebOptions
{
    /// <summary>
    /// Gets or sets the endpoint protocol and host (e.g. http://localhost).
    /// </summary>
    [Required, Url]
    public string Host { get; set; } = string.Empty; // TODO: [Issue #48] update to use System.Uri

    /// <summary>
    /// Gets or sets the endpoint port.
    /// </summary>
    [Required, Range(0, 65535)]
    public int Port { get; set; }
}
