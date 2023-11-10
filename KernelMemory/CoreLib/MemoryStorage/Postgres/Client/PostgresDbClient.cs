// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.KernelMemory.MemoryStorage.Qdrant.Client.Http;
using Microsoft.SemanticKernel.Diagnostics;
using Npgsql;
using NpgsqlTypes;
using Pgvector;

namespace Microsoft.KernelMemory.MemoryStorage.Postgres.Client;
public class PostgresDbClient : IPostgresDbClient
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly int _vectorSize;
    private readonly string _schema;
    private string queryColumns = "id, payload, tags, timestamp";


    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresDbClient"/> class.
    /// </summary>
    /// <param name="dataSource">Postgres data source.</param>
    /// <param name="schema">Schema of collection tables.</param>
    /// <param name="vectorSize">Embedding vector size.</param>
    public PostgresDbClient(NpgsqlDataSource dataSource, string schema, int vectorSize)
    {
        this._dataSource = dataSource;
        this._schema = schema;
        this._vectorSize = vectorSize;
    }

    public async Task CreateTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        NpgsqlConnection connection = await this._dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (connection)
        {
            using NpgsqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = $@"
                CREATE TABLE IF NOT EXISTS {this.GetFullTableName(tableName)} (
                    id TEXT NOT NULL,
                    payload JSONB,
                    tags JSONB,
                    embedding vector({this._vectorSize}),
                    timestamp TIMESTAMP WITH TIME ZONE,
                    PRIMARY KEY (id))";
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<string>> GetIndexesAsync(CancellationToken cancellationToken = default)
    {   
        return this.GetTablesAsync(cancellationToken).ToEnumerable();
    }

    public async Task DeleteAsync(string tableName, string key, CancellationToken cancellationToken = default)
    {
        NpgsqlConnection connection = await this._dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (connection)
        {
            using NpgsqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = $"DELETE FROM {this.GetFullTableName(tableName)} WHERE id=@key";
            cmd.Parameters.AddWithValue("@key", key);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task DeleteBatchAsync(string tableName, IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        string[] keysArray = keys.ToArray();
        if (keysArray.Length == 0)
        {
            return;
        }

        NpgsqlConnection connection = await this._dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (connection)
        {
            using NpgsqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = $"DELETE FROM {this.GetFullTableName(tableName)} WHERE id=ANY(@keys)";
            cmd.Parameters.AddWithValue("@keys", NpgsqlDbType.Array | NpgsqlDbType.Text, keysArray);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }


    public async Task DeleteTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        NpgsqlConnection connection = await this._dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (connection)
        {
            using NpgsqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = $"DROP TABLE IF EXISTS {this.GetFullTableName(tableName)}";

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<bool> DoesTableExistsAsync(string tableName, CancellationToken cancellationToken = default)
    {
        NpgsqlConnection connection = await this._dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (connection)
        {
            using NpgsqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = $@"
                SELECT table_name
                FROM information_schema.tables
                WHERE table_schema = @schema
                    AND table_type = 'BASE TABLE'
                    AND table_name = '{tableName}'";
            cmd.Parameters.AddWithValue("@schema", this._schema);

            using NpgsqlDataReader dataReader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await dataReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return dataReader.GetString(dataReader.GetOrdinal("table_name")) == tableName;
            }

            return false;
        }
    }

    public async IAsyncEnumerable<(PostgresMemoryRecord, double)> GetNearestMatchesAsync(string tableName, ICollection<MemoryFilter>? filters, Vector embedding, int limit, double minRelevanceScore = 0, bool withEmbeddings = false, CancellationToken cancellationToken = default)
    {
        if (withEmbeddings)
        {
            this.queryColumns = "*";
        }

        NpgsqlConnection connection = await this._dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var requiredTags = new List<IEnumerable<string>>();
        StringBuilder stringBuilder = new();
        foreach (var item in filters)
        {
            item.Pairs.ToList().ForEach(x => stringBuilder.Append($" AND (tags ->> '{x.Key}' = '[\"{x.Value}\"]')"));
        }

        await using (connection)
        {
            using NpgsqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = @$"
                SELECT * FROM (SELECT {this.queryColumns}, 1 - (embedding <=> @embedding) AS cosine_similarity FROM {this.GetFullTableName(tableName)}
                ) AS sk_memory_cosine_similarity_table
                WHERE cosine_similarity >= @min_relevance_score
                {stringBuilder}
                ORDER BY cosine_similarity DESC
                Limit @limit";
            cmd.Parameters.AddWithValue("@embedding", embedding);
            cmd.Parameters.AddWithValue("@collection", tableName);
            cmd.Parameters.AddWithValue("@min_relevance_score", minRelevanceScore);
            cmd.Parameters.AddWithValue("@limit", limit);

            using NpgsqlDataReader dataReader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            while (await dataReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                double cosineSimilarity = dataReader.GetDouble(dataReader.GetOrdinal("cosine_similarity"));
                yield return (await this.ReadEntryAsync(dataReader, withEmbeddings, cancellationToken).ConfigureAwait(false), cosineSimilarity);
            }
        }
    }

    public async IAsyncEnumerable<string> GetTablesAsync(CancellationToken cancellationToken = default)
    {
        NpgsqlConnection connection = await this._dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (connection)
        {
            using NpgsqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT table_name
                FROM information_schema.tables
                WHERE table_schema = @schema
                    AND table_type = 'BASE TABLE'";
            cmd.Parameters.AddWithValue("@schema", this._schema);

            using NpgsqlDataReader dataReader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await dataReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return dataReader.GetString(dataReader.GetOrdinal("table_name"));
            }
        }
    }

    
    public async IAsyncEnumerable<PostgresMemoryRecord> GetListAsync(string tableName, ICollection<MemoryFilter>? filters = null, int limit = 1, bool withEmbeddings = false, CancellationToken cancellationToken = default)
    {
        if (withEmbeddings)
        {
            this.queryColumns = "*";
        }

        NpgsqlConnection connection = await this._dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var requiredTags = new List<IEnumerable<string>>();
        StringBuilder stringBuilder = new();
        foreach (var item in filters)
        {
            item.Pairs.ToList().ForEach(x => stringBuilder.Append($" AND (tags ->> '{x.Key}' = '[\"{x.Value}\"]')"));
        }

        await using (connection)
        {
            using NpgsqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = @$"
                SELECT * FROM {this.GetFullTableName(tableName)}
                WHERE 1=1
                {stringBuilder}
                ORDER BY timestamp DESC
                Limit @limit";
            cmd.Parameters.AddWithValue("@collection", tableName);
            cmd.Parameters.AddWithValue("@limit", limit);

            using NpgsqlDataReader dataReader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            while (await dataReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return await this.ReadEntryAsync(dataReader, withEmbeddings, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task<PostgresMemoryRecord> ReadAsync(string tableName, string key, bool withEmbeddings = false, CancellationToken cancellationToken = default)
    {
        if (withEmbeddings)
        {
            this.queryColumns = "*";
        }

        NpgsqlConnection connection = await this._dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (connection)
        {
            using NpgsqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT {this.queryColumns} FROM {this.GetFullTableName(tableName)} WHERE id=@key";
            cmd.Parameters.AddWithValue("@key", key);

            using NpgsqlDataReader dataReader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await dataReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return await this.ReadEntryAsync(dataReader, withEmbeddings, cancellationToken).ConfigureAwait(false);
            }

            return null;
        }
    }

    public async IAsyncEnumerable<PostgresMemoryRecord> ReadBatchAsync(string tableName, IEnumerable<string> keys, bool withEmbeddings = false, CancellationToken cancellationToken = default)
    {
        string[] keysArray = keys.ToArray();
        if (keysArray.Length == 0)
        {
            yield break;
        }

        if (withEmbeddings)
        {
            this.queryColumns = "*";
        }

        NpgsqlConnection connection = await this._dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (connection)
        {
            using NpgsqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT {this.queryColumns} FROM {this.GetFullTableName(tableName)} WHERE id=ANY(@keys)";
            cmd.Parameters.AddWithValue("@keys", NpgsqlDbType.Array | NpgsqlDbType.Text, keysArray);

            using NpgsqlDataReader dataReader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await dataReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return await this.ReadEntryAsync(dataReader, withEmbeddings, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task UpsertAsync(string tableName, string key, string payload, string tags, Vector embedding, DateTime? timestamp, CancellationToken cancellationToken = default)
    {
        NpgsqlConnection connection = await this._dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (connection)
        {
            using NpgsqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = $@"
                INSERT INTO {this.GetFullTableName(tableName)} (id, payload, tags, embedding, timestamp)
                VALUES(@id, @payload, @tags, @embedding, @timestamp)
                ON CONFLICT (id)
                DO UPDATE SET payload=@payload, tags=@tags, embedding=@embedding, timestamp=@timestamp";
            cmd.Parameters.AddWithValue("@id", key);
            cmd.Parameters.AddWithValue("@payload", NpgsqlTypes.NpgsqlDbType.Jsonb, payload ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@tags", NpgsqlTypes.NpgsqlDbType.Jsonb, tags ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@embedding", embedding ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@timestamp", NpgsqlTypes.NpgsqlDbType.TimestampTz, timestamp ?? (object)DBNull.Value);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Read a entry.
    /// </summary>
    /// <param name="dataReader">The <see cref="NpgsqlDataReader"/> to read.</param>
    /// <param name="withEmbeddings">If true, the embeddings will be returned in the entries.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns></returns>
    private async Task<PostgresMemoryRecord> ReadEntryAsync(NpgsqlDataReader dataReader, bool withEmbeddings = false, CancellationToken cancellationToken = default)
    {
        string key = dataReader.GetString(dataReader.GetOrdinal("id"));

        Dictionary<string, object> payload = JsonSerializer.Deserialize<Dictionary<string, object>>(dataReader.GetString(dataReader.GetOrdinal("payload")));
        TagCollection tags = JsonSerializer.Deserialize<TagCollection>(dataReader.GetString(dataReader.GetOrdinal("tags")));

        Vector? embedding = withEmbeddings ? await dataReader.GetFieldValueAsync<Vector>(dataReader.GetOrdinal("embedding"), cancellationToken).ConfigureAwait(false) : null;
        DateTime? timestamp = await dataReader.GetFieldValueAsync<DateTime?>(dataReader.GetOrdinal("timestamp"), cancellationToken).ConfigureAwait(false);
        return new PostgresMemoryRecord() { Id = key, Payload = payload, Tags = tags, Vector = embedding, Timestamp = timestamp };
    }

    /// <summary>
    /// Get full table name with schema from table name.
    /// </summary>
    /// <param name="tableName"></param>
    /// <returns></returns>
    private string GetFullTableName(string tableName)
    {
        return $"{this._schema}.\"{tableName}\"";
    }
}
