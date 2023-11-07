// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json;
using Pgvector;

namespace Microsoft.KernelMemory.MemoryStorage.Postgres;
public class PostgresMemoryRecord
{
    public string Id { get; set; } = string.Empty;

    public Dictionary<string, object> Payload { get; set; } = new();

    public TagCollection Tags { get; set; } = new();

    public Vector? Vector { get; set; }

    public DateTime? Timestamp { get; set; }

    public string DictionnaryToJson(string name, Dictionary<string, object> input)
    {
        string result = "{\"" + name + "\": " + JsonSerializer.Serialize(input) + "}";
        return result;
    }

    public string TagCollectiontoJon(string name, TagCollection tags)
    {
        string result = "{\"" + name + "\": " + JsonSerializer.Serialize(tags) + "}";
        return result;
    }



}
