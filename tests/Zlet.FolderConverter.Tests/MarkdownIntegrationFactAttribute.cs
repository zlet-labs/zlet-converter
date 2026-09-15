using System;
using Xunit;
using Zlet.FolderConverter.Core.Services;

namespace Zlet.FolderConverter.Tests;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class MarkdownIntegrationFactAttribute : FactAttribute
{
    public MarkdownIntegrationFactAttribute()
    {
        var runner = new AnydocWorkerProcessRunner();
        if (!runner.IsAvailable)
        {
            Skip = "Markdown runtime is not available (native anydoc worker binary not found).";
        }
    }
}
