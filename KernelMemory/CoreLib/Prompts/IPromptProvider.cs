// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.KernelMemory.Prompts;
public interface IPromptProvider
{
    /// <summary>
    /// Return a prompt content
    /// </summary>
    /// <param name="promptName">Prompt name</param>
    /// <returns>Prompt string</returns>
    public string ReadPrompt(string promptName);
}
