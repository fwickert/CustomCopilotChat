// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.KernelMemory.MemoryStorage.Postgres;
public class PostgresConfig
{
    public string ConnectionString { get; set; } = string.Empty;

    public int VectorSize { get; set; } = 1536;
}
