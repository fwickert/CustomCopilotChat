﻿// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel.DataAnnotations;

namespace CopilotChat.WebApi.Options;

/// <summary>
/// Configuration options for the CopilotChat service.
/// </summary>
public class ServiceOptions
{
    public const string PropertyName = "Service";

    /// <summary>
    /// Timeout limit on requests to the service in seconds.
    /// </summary>
    [Range(0, int.MaxValue)]
    public double? TimeoutLimitInS { get; set; }

    /// <summary>
    /// Configuration Key Vault URI
    /// </summary>
    [Url]
    public string? KeyVault { get; set; }

    /// <summary>
    /// Local directory in which to load semantic skills.
    /// </summary>
    public string? SemanticSkillsDirectory { get; set; }

    /// <summary>
    /// Setting indicating if the site is undergoing maintenance.
    /// </summary>
    public bool InMaintenance { get; set; }
}
