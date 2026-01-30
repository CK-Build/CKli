using System;

namespace CKli.Core.GitHosting.Providers;

internal interface IInternalPluginGitHostingProvider : IDisposable
{
    IInternalPluginGitHostingProvider? Next { get; set; }

    IInternalPluginGitHostingProvider? Prev { get; set; }

    IGitRepositoryAccessKey GitKey { get; }

    string ToString();
}
