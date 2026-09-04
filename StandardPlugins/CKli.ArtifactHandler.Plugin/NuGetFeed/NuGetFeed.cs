using CK.Core;
using System.Xml.Linq;

namespace CKli.ArtifactHandler.Plugin;

/// <summary>
/// Models a NuGet feed.
/// <para>
/// The credentials drives the behavior:
/// <list type="bullet">
///     <item>
///     When both <see cref="PublicReadCredentials"/> and <see cref="Credentials"/> are null, this is a true read-only public feed (<c>https://nuget.org</c>).
///     </item>
///     <item>
///     When <see cref="PublicReadCredentials"/> is set, this is a public feed that requires an authentication (for tracking, rate limiting,, etc.).
///     This is the case of GitHub public feeds. The credentials appear in clear text in the <c>nuget.config</c> file and is committed.
///     </item>
///     <item>
///     When <see cref="Credentials"/> is set, this is either:
///     <list type="bullet">
///         <item>If <see cref="PushQualityFilter"/> is specified, this is a public or private feed into which packages can be pushed.</item>
///         <item>If <see cref="PushQualityFilter"/> is not specified, this is a read-only private feed.</item>
///     </list>
///     The credentials don't appear in the <c>nuget.config</c> file.
///     </item>
/// </list>
/// </para>
/// </summary>
public sealed class NuGetFeed
{
    readonly string _name;
    readonly NormalizedPath _url;
    readonly NuGetFeedCredentials? _credentials;
    readonly NuGetFeedCredentials? _publicReadCredentials;
    readonly CSVersionKindFilter? _pushQualityFilter;

    /// <summary>
    /// Initializes a new NuGet feed.
    /// </summary>
    /// <param name="name">The feed name.</param>
    /// <param name="url">The url to the NuGet feed</param>
    /// <param name="credentials">See <see cref="Credentials"/>.</param>
    /// <param name="pushQualityFilter">See <see cref="PushQualityFilter"/>.</param>
    /// <param name="publicReadCredentials">See <see cref="PublicReadCredentials"/>.</param>
    public NuGetFeed( string name,
                      NormalizedPath url,
                      NuGetFeedCredentials? credentials,
                      CSVersionKindFilter? pushQualityFilter,
                      NuGetFeedCredentials? publicReadCredentials )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( name );
        Throw.CheckArgument( !url.IsEmptyPath );
        Throw.CheckArgument( "The fake credentials must not be an API key.",
                              publicReadCredentials == null || publicReadCredentials.UserNameKey != null );
        _name = name;
        _url = url;
        _credentials = credentials;
        _publicReadCredentials = publicReadCredentials;
        _pushQualityFilter = pushQualityFilter;
    }

    internal static NuGetFeed Create( XElement e )
    {
        var name = (string)e.Attribute( XNames.Name )!;
        var url = (string?)e.Attribute( XNames.Url );
        CSVersionKindFilter? q = null;
        var sQ = (string?)e.Attribute( XNames.PushQualityFilter );
        if( !string.IsNullOrWhiteSpace( sQ ) )
        {
            if( CSVersionKindFilter.TryParse( sQ, out var qf ) )
            {
                q = qf;
            }
            else
            {
                Throw.ArgumentException( nameof( PushQualityFilter ) );
            }
        }
        var p = NuGetFeedCredentials.Create( e.Element( XNames.Credentials ) );
        var r = NuGetFeedCredentials.Create( e.Element( XNames.PublicReadCredentials ) );
        return new NuGetFeed( name, url, p, q, r );
    }

    internal XElement ToXml() => new XElement( XNames.Feed,
                                               new XAttribute( XNames.Name, _name ),
                                               new XAttribute( XNames.Url, _url ),
                                               _pushQualityFilter.HasValue ? new XAttribute( XNames.PushQualityFilter, _pushQualityFilter ) : null,
                                               _credentials?.ToXml( XNames.Credentials ),
                                               _publicReadCredentials?.ToXml( XNames.PublicReadCredentials ) );

    /// <summary>
    /// The feed name.
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// The url to the NuGet feed (as it appears in the <c>nuget.config</c> file).
    /// </summary>
    public NormalizedPath Url => _url;

    /// <summary>
    /// An optional public credentials that allows to make any private NuGet feed de facto
    /// public: these credentials are written as-is in the repositories <c>nuget.config</c>
    /// files. See https://learn.microsoft.com/en-us/nuget/reference/nuget-config-file#packagesourcecredentials.
    /// <para>
    /// This workarounds NuGet servers that even for public feeds (of public packages) require
    /// an authentication.
    /// This is the case of <see href="https://docs.github.com/fr/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry#authenticating-with-a-personal-access-token">GitHub</see>.
    /// </para>
    /// <para>
    /// This PublicReadCredentials and <see cref="Credentials"/> are mutually exclusive.
    /// </para>
    /// </summary>
    public NuGetFeedCredentials? PublicReadCredentials => _publicReadCredentials;

    /// <summary>
    /// The required credentials to access a private feed or to push packages in a public one.
    /// When not specified:
    /// <list type="bullet">
    ///     <item>CKli will never try to push any package to this feed.</item>
    ///     <item>The feed must be a true public feed or <see cref="PublicReadCredentials"/> must be set.</item>
    /// </list>
    /// <para>
    /// This Credentials and <see cref="PublicReadCredentials"/> are mutually exclusive.
    /// </para>
    /// <para>
    /// Notes to developers/testers of CKli or plugins: This also applies to a local folder feed.
    /// To allow write to the local folder, you can use the same key as the one required
    /// to push to a local git repository (that is "FILESYSTEM_GIT") and register
    /// a "don't care" value in the secret store:
    /// <code>
    /// dotnet user-secrets set FILESYSTEM_GIT "don't care" --id CKli-CKli-Test
    /// </code>
    /// Note: the name use here (<c>CKli-CKli-Test</c>) depends on the test host that is running.
    /// </para>
    /// </summary>
    public NuGetFeedCredentials? Credentials => _credentials;

    /// <summary>
    /// Gets the filter that can restrict pushed versions of packages into this feed.
    /// Defaults to null: the feed is a read only feed (<see cref="CanPush(CSVersionKind, bool)"/> always returns false).
    /// To accept all versions, "[,].ci" must be specified.
    /// </summary>
    public CSVersionKindFilter? PushQualityFilter => _pushQualityFilter;


    /// <summary>
    /// Checks whether a kind of packages can be pushed to this feed:
    /// <list type="bullet">
    ///    <item>The <see cref="Credentials"/> must exist and <see cref="NuGetFeedCredentials.IsAPIKey"/> must be true.</item>
    ///    <item>The <see cref="PushQualityFilter"/> must be not null and <see cref="CSVersionKindFilter.Accepts(CSVersionKind, bool)"/> must be true.</item>
    /// </list>
    /// </summary>
    /// <param name="kind">The version kind to challenge.</param>
    /// <param name="isCI">Whether a CI version must be considered.</param>
    /// <returns>Whether <paramref name="kind"/> and <paramref name="isCI"/> are accepted or not.</returns>
    public bool CanPush( CSVersionKind kind, bool isCI ) => _credentials != null
                                                            && _credentials.IsAPIKey
                                                            && _pushQualityFilter.HasValue
                                                            && _pushQualityFilter.Value.Accepts( kind, isCI );

    /// <summary>
    /// Returns the "Name (Url)".
    /// </summary>
    /// <returns>The "Name (Url)".</returns>
    public override string ToString() => $"{Name} ({Url})";
}
