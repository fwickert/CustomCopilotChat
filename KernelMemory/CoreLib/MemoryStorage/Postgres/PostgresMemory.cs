// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory.Diagnostics;
using Pgvector.Npgsql;
using Microsoft.KernelMemory.MemoryStorage.Postgres.Client;
using System.Text.Json;


namespace Microsoft.KernelMemory.MemoryStorage.Postgres;
public class PostgresMemory : IVectorDb
{
    private readonly ILogger<PostgresMemory> _log;
    //private readonly PostgresMemoryStore _postgresMemoryStore;
    private readonly PostgresDbClient _postgresDbClient;

    public PostgresMemory(PostgresConfig config, ILogger<PostgresMemory>? log = null)
    {
        this._log = log ?? DefaultLogger<PostgresMemory>.Instance;
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(config.ConnectionString);
        dataSourceBuilder.UseVector();

        this._postgresDbClient = new PostgresDbClient(dataSourceBuilder.Build(), "public", config.VectorSize);
    }

    public Task CreateIndexAsync(string index, int vectorSize, CancellationToken cancellationToken = default)
    {

        return this._postgresDbClient.CreateTableAsync(index, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<string>> GetIndexesAsync(CancellationToken cancellationToken = default)
    {
        return await this._postgresDbClient
            .GetIndexesAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DeleteAsync(string index, MemoryRecord record, CancellationToken cancellationToken = default)
    {
        PostgresMemoryRecord existingRecord = await this._postgresDbClient.ReadAsync(index, record.Id, false, cancellationToken)
            .ConfigureAwait(false);

        if (existingRecord == null)
        {
            this._log.LogTrace("No record with ID {0} found, nothing to delete", record.Id);
            return;
        }

        this._log.LogTrace("Point ID {0} found, deleting...", existingRecord.Id);
        await this._postgresDbClient.DeleteAsync(index, record.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task DeleteIndexAsync(string index, CancellationToken cancellationToken = default)
    {
        if (string.Equals(index, Constants.DefaultIndex, StringComparison.OrdinalIgnoreCase))
        {
            this._log.LogWarning("The default index cannot be deleted");
            return Task.CompletedTask;
        }

        return this._postgresDbClient.DeleteTableAsync(index, cancellationToken);
    }

    public async IAsyncEnumerable<MemoryRecord> GetListAsync(string index, ICollection<MemoryFilter> filters = null, int limit = 1, bool withEmbeddings = false, CancellationToken cancellationToken = default)
    {
        //On découpe l'indexname et on prend le 1er elément pour faire 

        if (limit <= 0) { limit = int.MaxValue; }

        var result = this._postgresDbClient.GetListAsync(index, filters, limit, withEmbeddings, cancellationToken);
        IAsyncEnumerator<PostgresMemoryRecord> items = result.GetAsyncEnumerator();

        while (await items.MoveNextAsync())
        {
            MemoryRecord memoryRecord = new()
            {
                Id = items.Current.Id,
                Payload = items.Current.Payload,
                Tags = items.Current.Tags
            };

            yield return (memoryRecord);
        }
    }

    public async IAsyncEnumerable<(MemoryRecord, double)> GetSimilarListAsync(
        string index,
        Embedding embedding,
        ICollection<MemoryFilter> filters = null,
        double minRelevance = 0,
        int limit = 1,
        bool withEmbeddings = false,
        CancellationToken cancellationToken = default)
    {

        if (limit <= 0) { limit = int.MaxValue; }

        Pgvector.Vector vector = new(embedding.Data.ToArray());
        var result = this._postgresDbClient.GetNearestMatchesAsync(index, filters, vector, limit, minRelevance, withEmbeddings, cancellationToken);
        IAsyncEnumerator<(PostgresMemoryRecord, double)> items = result.GetAsyncEnumerator();

        while (await items.MoveNextAsync())
        {
            MemoryRecord memoryRecord = new()
            {
                Id = items.Current.Item1.Id,
                Payload = items.Current.Item1.Payload,
                Tags = items.Current.Item1.Tags,
                Vector = new Embedding(embedding.Data)
            };

            yield return (memoryRecord, items.Current.Item2);
        }

    }

    public async Task<string> UpsertAsync(string index, MemoryRecord record, CancellationToken cancellationToken = default)
    {
        string payload = JsonSerializer.Serialize(record.Payload, s_jsonOptions);
        string tags = JsonSerializer.Serialize(record.Tags, s_jsonOptions);
        Pgvector.Vector vector = new(record.Vector.Data.ToArray());

        await this._postgresDbClient.UpsertAsync(index, record.Id, payload, tags, vector, DateTime.UtcNow, cancellationToken);

        return record.Id;
    }

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        AllowTrailingCommas = true,
        MaxDepth = 10,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        WriteIndented = false,
    };
}
