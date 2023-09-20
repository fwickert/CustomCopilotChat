﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Azure;
using CopilotChat.WebApi.Auth;
using CopilotChat.WebApi.Models.Storage;
using CopilotChat.WebApi.Options;
using CopilotChat.WebApi.Services;
using CopilotChat.WebApi.Storage;
using CopilotChat.WebApi.Utilities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Tesseract;

namespace CopilotChat.WebApi.Extensions;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/>.
/// Add options and services for Copilot Chat.
/// </summary>
public static class CopilotChatServiceExtensions
{
    /// <summary>
    /// Parse configuration into options.
    /// </summary>
    public static IServiceCollection AddOptions(this IServiceCollection services, ConfigurationManager configuration)
    {
        // General configuration
        AddOptions<ServiceOptions>(ServiceOptions.PropertyName);

        // Default AI service configurations for Semantic Kernel
        AddOptions<AIServiceOptions>(AIServiceOptions.PropertyName);

        // Memory store configuration
        AddOptions<MemoryStoreOptions>(MemoryStoreOptions.PropertyName);

        // Authentication configuration
        AddOptions<ChatAuthenticationOptions>(ChatAuthenticationOptions.PropertyName);

        // Chat log storage configuration
        AddOptions<ChatStoreOptions>(ChatStoreOptions.PropertyName);

        // Azure speech token configuration
        AddOptions<AzureSpeechOptions>(AzureSpeechOptions.PropertyName);

        // Bot schema configuration
        AddOptions<BotSchemaOptions>(BotSchemaOptions.PropertyName);

        // Document memory options
        AddOptions<DocumentMemoryOptions>(DocumentMemoryOptions.PropertyName);

        // Chat prompt options
        AddOptions<PromptsOptions>(PromptsOptions.PropertyName);

        // Planner options
        AddOptions<PlannerOptions>(PlannerOptions.PropertyName);

        // OCR support options
        AddOptions<OcrSupportOptions>(OcrSupportOptions.PropertyName);

        // Content safety options
        AddOptions<ContentSafetyOptions>(ContentSafetyOptions.PropertyName);

        return services;

        void AddOptions<TOptions>(string propertyName)
            where TOptions : class
        {
            services.AddOptions<TOptions>(configuration.GetSection(propertyName));
        }
    }

    internal static void AddOptions<TOptions>(this IServiceCollection services, IConfigurationSection section)
        where TOptions : class
    {
        services.AddOptions<TOptions>()
            .Bind(section)
            .ValidateDataAnnotations()
            .ValidateOnStart()
            .PostConfigure(TrimStringProperties);
    }

    internal static IServiceCollection AddUtilities(this IServiceCollection services)
    {
        return services.AddScoped<AskConverter>();
    }

    /// <summary>
    /// Add CORS settings.
    /// </summary>
    internal static IServiceCollection AddCorsPolicy(this IServiceCollection services)
    {
        IConfiguration configuration = services.BuildServiceProvider().GetRequiredService<IConfiguration>();
        string[] allowedOrigins = configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        if (allowedOrigins.Length > 0)
        {
            services.AddCors(options =>
            {
                options.AddDefaultPolicy(
                    policy =>
                    {
                        policy.WithOrigins(allowedOrigins)
                            .WithMethods("GET", "POST", "DELETE")
                            .AllowAnyHeader();
                    });
            });
        }

        return services;
    }

    /// <summary>
    /// Adds persistent OCR support service.
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    public static IServiceCollection AddPersistentOcrSupport(this IServiceCollection services)
    {
        OcrSupportOptions ocrSupportConfig = services.BuildServiceProvider().GetRequiredService<IOptions<OcrSupportOptions>>().Value;

        switch (ocrSupportConfig.Type)
        {
            case OcrSupportOptions.OcrSupportType.AzureFormRecognizer:
            {
                services.AddSingleton<IOcrEngine>(sp => new AzureFormRecognizerOcrEngine(ocrSupportConfig.AzureFormRecognizer!.Endpoint!, new AzureKeyCredential(ocrSupportConfig.AzureFormRecognizer!.Key!)));
                break;
            }
            case OcrSupportOptions.OcrSupportType.Tesseract:
            {
                services.AddSingleton<IOcrEngine>(sp => new TesseractEngineWrapper(new TesseractEngine(ocrSupportConfig.Tesseract!.FilePath, ocrSupportConfig.Tesseract!.Language, EngineMode.Default)));
                break;
            }
            case OcrSupportOptions.OcrSupportType.None:
            {
                services.AddSingleton<IOcrEngine>(sp => new NullOcrEngine());
                break;
            }
            default:
            {
                throw new InvalidOperationException($"Unsupported OcrSupport:Type '{ocrSupportConfig.Type}'");
            }
        }

        return services;
    }

    /// <summary>
    /// Add persistent chat store services.
    /// </summary>
    public static IServiceCollection AddPersistentChatStore(this IServiceCollection services)
    {
        IStorageContext<ChatSession> chatSessionStorageContext;
        IStorageContext<ChatMessage> chatMessageStorageContext;
        IStorageContext<MemorySource> chatMemorySourceStorageContext;
        IStorageContext<ChatParticipant> chatParticipantStorageContext;
        //Custom
        IStorageContext<ChatTokensUsage> chatTokensUsageStorageContext;

        ChatStoreOptions chatStoreConfig = services.BuildServiceProvider().GetRequiredService<IOptions<ChatStoreOptions>>().Value;

        switch (chatStoreConfig.Type)
        {
            case ChatStoreOptions.ChatStoreType.Volatile:
            {
                chatSessionStorageContext = new VolatileContext<ChatSession>();
                chatMessageStorageContext = new VolatileContext<ChatMessage>();
                chatMemorySourceStorageContext = new VolatileContext<MemorySource>();
                chatParticipantStorageContext = new VolatileContext<ChatParticipant>();
                //Custom
                chatTokensUsageStorageContext = new VolatileContext<ChatTokensUsage>();
                break;
            }

            case ChatStoreOptions.ChatStoreType.Filesystem:
            {
                if (chatStoreConfig.Filesystem == null)
                {
                    throw new InvalidOperationException("ChatStore:Filesystem is required when ChatStore:Type is 'Filesystem'");
                }

                string fullPath = Path.GetFullPath(chatStoreConfig.Filesystem.FilePath);
                string directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
                chatSessionStorageContext = new FileSystemContext<ChatSession>(
                    new FileInfo(Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(fullPath)}_sessions{Path.GetExtension(fullPath)}")));
                chatMessageStorageContext = new FileSystemContext<ChatMessage>(
                    new FileInfo(Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(fullPath)}_messages{Path.GetExtension(fullPath)}")));
                chatMemorySourceStorageContext = new FileSystemContext<MemorySource>(
                    new FileInfo(Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(fullPath)}_memorysources{Path.GetExtension(fullPath)}")));
                chatParticipantStorageContext = new FileSystemContext<ChatParticipant>(
                    new FileInfo(Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(fullPath)}_participants{Path.GetExtension(fullPath)}")));
                //Custom
                chatTokensUsageStorageContext = new FileSystemContext<ChatTokensUsage>(
                                       new FileInfo(Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(fullPath)}_tokensusage{Path.GetExtension(fullPath)}")));
                break;
            }

            case ChatStoreOptions.ChatStoreType.Cosmos:
            {
                if (chatStoreConfig.Cosmos == null)
                {
                    throw new InvalidOperationException("ChatStore:Cosmos is required when ChatStore:Type is 'Cosmos'");
                }
#pragma warning disable CA2000 // Dispose objects before losing scope - objects are singletons for the duration of the process and disposed when the process exits.
                chatSessionStorageContext = new CosmosDbContext<ChatSession>(
                    chatStoreConfig.Cosmos.ConnectionString, chatStoreConfig.Cosmos.Database, chatStoreConfig.Cosmos.ChatSessionsContainer);
                chatMessageStorageContext = new CosmosDbContext<ChatMessage>(
                    chatStoreConfig.Cosmos.ConnectionString, chatStoreConfig.Cosmos.Database, chatStoreConfig.Cosmos.ChatMessagesContainer);
                chatMemorySourceStorageContext = new CosmosDbContext<MemorySource>(
                    chatStoreConfig.Cosmos.ConnectionString, chatStoreConfig.Cosmos.Database, chatStoreConfig.Cosmos.ChatMemorySourcesContainer);
                chatParticipantStorageContext = new CosmosDbContext<ChatParticipant>(
                    chatStoreConfig.Cosmos.ConnectionString, chatStoreConfig.Cosmos.Database, chatStoreConfig.Cosmos.ChatParticipantsContainer);
                //CUSTOM
                chatTokensUsageStorageContext = new CosmosDbContext<ChatTokensUsage>(
                                       chatStoreConfig.Cosmos.ConnectionString, chatStoreConfig.Cosmos.Database, chatStoreConfig.Cosmos.ChatTokensUsageContainer);
#pragma warning restore CA2000 // Dispose objects before losing scope
                break;
            }

            default:
            {
                throw new InvalidOperationException(
                    "Invalid 'ChatStore' setting 'chatStoreConfig.Type'.");
            }
        }

        services.AddSingleton<ChatSessionRepository>(new ChatSessionRepository(chatSessionStorageContext));
        services.AddSingleton<ChatMessageRepository>(new ChatMessageRepository(chatMessageStorageContext));
        services.AddSingleton<ChatMemorySourceRepository>(new ChatMemorySourceRepository(chatMemorySourceStorageContext));
        services.AddSingleton<ChatParticipantRepository>(new ChatParticipantRepository(chatParticipantStorageContext));
        //Custom
        services.AddSingleton<ChatTokensUsageRepository>(new ChatTokensUsageRepository(chatTokensUsageStorageContext));

        return services;
    }

    /// <summary>
    /// Add authorization services
    /// </summary>
    public static IServiceCollection AddCopilotChatAuthorization(this IServiceCollection services)
    {
        return services.AddScoped<IAuthorizationHandler, ChatParticipantAuthorizationHandler>()
            .AddAuthorizationCore(options =>
            {
                options.DefaultPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
                options.AddPolicy(AuthPolicyName.RequireChatParticipant, builder =>
                {
                    builder.RequireAuthenticatedUser()
                        .AddRequirements(new ChatParticipantRequirement());
                });
            });
    }

    /// <summary>
    /// Add authentication services
    /// </summary>
    public static IServiceCollection AddCopilotChatAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IAuthInfo, AuthInfo>();
        var config = services.BuildServiceProvider().GetRequiredService<IOptions<ChatAuthenticationOptions>>().Value;
        switch (config.Type)
        {
            case ChatAuthenticationOptions.AuthenticationType.AzureAd:
                services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                    .AddMicrosoftIdentityWebApi(configuration.GetSection($"{ChatAuthenticationOptions.PropertyName}:AzureAd"));
                break;

            case ChatAuthenticationOptions.AuthenticationType.None:
                services.AddAuthentication(PassThroughAuthenticationHandler.AuthenticationScheme)
                    .AddScheme<AuthenticationSchemeOptions, PassThroughAuthenticationHandler>(
                        authenticationScheme: PassThroughAuthenticationHandler.AuthenticationScheme,
                        configureOptions: null);
                break;

            default:
                throw new InvalidOperationException($"Invalid authentication type '{config.Type}'.");
        }

        return services;
    }

    /// <summary>
    /// Trim all string properties, recursively.
    /// </summary>
    private static void TrimStringProperties<T>(T options) where T : class
    {
        Queue<object> targets = new();
        targets.Enqueue(options);

        while (targets.Count > 0)
        {
            object target = targets.Dequeue();
            Type targetType = target.GetType();
            foreach (PropertyInfo property in targetType.GetProperties())
            {
                // Skip enumerations
                if (property.PropertyType.IsEnum)
                {
                    continue;
                }

                // Property is a built-in type, readable, and writable.
                if (property.PropertyType.Namespace == "System" &&
                    property.CanRead &&
                    property.CanWrite)
                {
                    // Property is a non-null string.
                    if (property.PropertyType == typeof(string) &&
                        property.GetValue(target) != null)
                    {
                        property.SetValue(target, property.GetValue(target)!.ToString()!.Trim());
                    }
                }
                else
                {
                    // Property is a non-built-in and non-enum type - queue it for processing.
                    if (property.GetValue(target) != null)
                    {
                        targets.Enqueue(property.GetValue(target)!);
                    }
                }
            }
        }
    }
}
