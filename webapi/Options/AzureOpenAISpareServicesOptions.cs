// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.KernelMemory;
namespace CopilotChat.WebApi.Options;

public class AzureOpenAISpareServicesOptions
{
    public const string PropertyName = "AzureOpenAISpareServices";

    public bool Activate { get; set; }

    public List<SpareService> SpareServices { get; set; } = new List<SpareService>();

    public List<SpareService>? GetServiceConfig<T>(IConfiguration cfg, string serviceName, string root = "AzureOpenAISpareServices")
    {
        List<SpareService> services = new();

        cfg.GetSection(root)?.GetSection("Services")?.Bind(services);

        return services;
    }
}

public class SpareService : AzureOpenAIConfig
{
    public string ServiceId { get; set; } = string.Empty;

    public bool Activate { get; set; }
}
