using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Shared <see cref="HttpClient"/>.
/// See <see href="https://learn.microsoft.com/fr-fr/dotnet/fundamentals/runtime-libraries/system-net-http-httpclient"/>.
/// </summary>
sealed class SharedHttpClient
{
    /// <summary>
    /// Gets a HttpClient that has a <see cref="Timeout.InfiniteTimeSpan"/> timeout: the 
    /// </summary>
    public static readonly HttpClient Instance;

    static SharedHttpClient()
    {
        Instance = new HttpClient() { Timeout = Timeout.InfiniteTimeSpan };
        Instance.DefaultRequestHeaders.UserAgent.Add( new ProductInfoHeaderValue( "CKli-GitHosting", "1.0" ) );
    }
}
