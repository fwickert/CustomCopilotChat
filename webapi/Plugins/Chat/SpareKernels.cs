// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using Microsoft.SemanticKernel;

namespace CopilotChat.WebApi.Plugins.Chat;

public class SpareKernels
{

    public List<IKernel> Kernels { get; set; } = new List<IKernel>();
}
