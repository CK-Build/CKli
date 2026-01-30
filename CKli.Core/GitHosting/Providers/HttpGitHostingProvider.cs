using System;

namespace CKli.Core.GitHosting.Providers;

/// <summary>
/// Base class for remote providers via http.
/// <para>
/// Cannot be used directly: <see cref="PluginHttpGitHostingProvider"/> must be used.
/// </para>
/// </summary>
public abstract class HttpGitHostingProvider : GitHostingProvider
{
    readonly Uri _baseApiUrl;

    private protected HttpGitHostingProvider( string baseUrl,
                                              KnownCloudGitProvider cloudGitProvider,
                                              IGitRepositoryAccessKey gitKey,
                                              Uri baseApiUrl )
        : base( baseUrl, cloudGitProvider, gitKey )
    {
        _baseApiUrl = baseApiUrl;
    }

    public Uri BaseApiUrl => _baseApiUrl;
}
