// Copyright (c) Microsoft. All rights reserved.


using Microsoft.Extensions.DependencyInjection;


namespace Microsoft.KernelMemory.MemoryStorage.Postgres;
public static partial class KernelMemoryBuilderExtensions
{
    public static KernelMemoryBuilder WithPostgres(this KernelMemoryBuilder builder, PostgresConfig config)
    {
        builder.Services.AddPostgresAsVectorDb(config);
        return builder;
    }

    public static KernelMemoryBuilder WithPostgres(this KernelMemoryBuilder builder, string connectionString)
    {
        builder.Services.AddPostgresAsVectorDb(connectionString);
        return builder;
    }
}

public static partial class DependencyInjection
{
    public static IServiceCollection AddPostgresAsVectorDb(this IServiceCollection services, PostgresConfig config)
    {
        return services
            .AddSingleton<PostgresConfig>(config)
            .AddSingleton<IVectorDb, PostgresMemory>();
    }

    public static IServiceCollection AddPostgresAsVectorDb(this IServiceCollection services, string connectionString)
    {
        var config = new PostgresConfig { ConnectionString = connectionString };
        return services.AddPostgresAsVectorDb(config);
    }
}
