﻿// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel.DataAnnotations;

namespace CopilotChat.WebApi.Options;

/// <summary>
/// Configuration settings for connecting to Qdrant.
/// </summary>
public class QdrantOptions : VectorMemoryWebOptions
{
    /// <summary>
    /// Gets or sets the vector size.
    /// </summary>
    [Required, Range(1, int.MaxValue)]
    public int VectorSize { get; set; }

    /// <summary>
    /// Gets or sets the Qdrant Cloud "api-key" header value.
    /// </summary>
    public string Key { get; set; } = string.Empty;
}
