using CK.Core;
using CKli.BranchModel.Plugin;
using CKli.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace CKli.ArtifactHandler.Plugin;

/// <summary>
/// Handles artifacts (NuGet packages and assets).
/// </summary>
public sealed class ArtifactHandlerPlugin : PrimaryRepoPlugin<RepoArtifactInfo>
{
    /// <summary>
    /// Reserved optional top folder name in project.
    /// </summary>
    public const string DeployFolderName = "Deployment";

    /// <summary>
    /// Reserved optional folder name in project's <see cref="DeployFolderName"/> folder.
    /// </summary>
    public const string DeployAssetsName = "Assets";


    readonly NormalizedPath _localNuGetPath;
    readonly NormalizedPath _localAssetsPath;
    ImmutableArray<NuGetFeed> _feeds;
    XDocument? _defaultNugetConfig;

    /// <summary>
    /// Initialize a new ArtifactHandlerPlugin.
    /// </summary>
    /// <param name="context">The CKli plugin context.</param>
    /// <param name="branchModel">The branch model plugin.</param>
    public ArtifactHandlerPlugin( PrimaryPluginContext context, BranchModelPlugin branchModel )
        : base( context )
    {
        _localNuGetPath = World.Name.LocalDataFolder.AppendPart( "NuGet" );
        _localAssetsPath = World.Name.LocalDataFolder.AppendPart( DeployAssetsName );
        Directory.CreateDirectory( _localNuGetPath );
        Directory.CreateDirectory( _localAssetsPath );
        branchModel.ContentIssue += HandleNuGetConfig;
    }

    void HandleNuGetConfig( ContentIssueEventArgs ev )
    {
        NormalizedPath n = "nuget.config";
        var nInfo = ev.Content.GetFileInfo( n );
        if( nInfo == null )
        {
            var defaultConfig = GetDefaultNuGetConfig( ev.Monitor );
            if( defaultConfig != null )
            {
                ev.Issues.CreateFile( n, defaultConfig.ToString );
            }
        }
        else
        {
            if( nInfo.Name != n )
            {
                Throw.DebugAssert( nInfo.Name.Equals( n, StringComparison.OrdinalIgnoreCase ) );
                ev.Issues.MoveFile( nInfo.Name, n );
            }
            using( var s = nInfo.CreateReadStream() )
            {
                var root = NuGetHelper.GetConfigurationRoot( ev.Monitor, s );
                if( root != null )
                {
                    if( ApplyConfiguredNuGetFeeds( ev.Monitor, root, out var actions )
                        && actions != null )
                    {
                        ev.Issues.UpdateFile( n, () => root.ToString() );
                    }
                }
            }
        }
    }

    /// <summary>
    /// Gets the "<see cref="LocalWorldName.LocalDataFolder"/>/NuGet" folder.
    /// </summary>
    public NormalizedPath LocalNuGetPath => _localNuGetPath;

    /// <summary>
    /// Gets the "<see cref="LocalWorldName.LocalDataFolder"/>/Assets" folder.
    /// </summary>
    public NormalizedPath LocalAssetsPath => _localAssetsPath;

    /// <summary>
    /// Gets the &lt;ArtifactHandler&gt;/&lt;NuGet&gt;/&lt;Feed&gt; configurations.
    /// <para>
    /// This is cached once obtained.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="feeds">The configured feeds.</param>
    /// <returns>True on success, false on error.</returns>
    public bool GetConfiguredNuGetFeeds( IActivityMonitor monitor, out ImmutableArray<NuGetFeed> feeds )
    {
        feeds = _feeds;
        if( feeds.IsDefault )
        {
            try
            {
                feeds = PrimaryPluginContext.Configuration.XElement.Elements( XNames.NuGet )
                                                           .Elements( XNames.Feed )
                                                           .Select( NuGetFeed.Create )
                                                           .ToImmutableArray();
                // Handling default here: when there's no configured feeds, we automatically add
                // the nuget.org public feed.
                if( feeds.Length == 0 )
                {
                    var nugetOrg = new NuGetFeed( "NuGet",
                                                  "https://api.nuget.org/v3/index.json",
                                                  credentials: new NuGetFeedCredentials( "NUGET_ORG_PUSH_API_KEY", null ),
                                                  pushQualityFilter: new CSVersionKindFilter( CSVersionKind.Papa, CSVersionKind.Stable, allowCI: false ),
                                                  publicReadCredentials: null );
                    PrimaryPluginContext.Configuration.Edit( monitor, ( monitor, e ) =>
                    {
                        e.Ensure( XNames.NuGet ).Add( nugetOrg.ToXml() );
                    } );
                    feeds = [nugetOrg];
                    monitor.Info( $"ArtifactHandler plugin configuration has been Initialized with 'https://nuget.org' feed." );
                }
                _feeds = feeds;
            }
            catch( Exception ex )
            {
                monitor.Error( $"Error while reading <ArtifactHandler>. This must be fixed manually.", ex );
                feeds = default;
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Applies the <see cref="NuGetFeed"/> configurations to a <c>nuget.config</c> file.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="root">The &lt;configuration&gt; root of the <c>nuget.config</c> file.</param>
    /// <param name="actions">Outputs the required updates if any. Null if the <paramref name="root"/> is up to date.</param>
    /// <param name="withSecretCredentials">
    /// When specified, consider the <see cref="NuGetFeed.Credentials"/>, resolves the secret and writes the NuGet credentials: this
    /// must be a temporary uncommitted change.
    /// </param>
    /// <returns>True on success, false otherwise.</returns>
    public bool ApplyConfiguredNuGetFeeds( IActivityMonitor monitor, XElement root, out List<string>? actions, ISecretsStore? withSecretCredentials = null )
    {
        actions = null;
        if( !GetConfiguredNuGetFeeds( monitor, out var feeds ) )
        {
            return false;
        }
        var addSources = root.Elements( NuGetHelper.XNames.PackageSources ).Elements( NuGetHelper.XNames.Add ).ToList();
        var mappings = root.Elements( NuGetHelper.XNames.PackageSourceMapping ).Elements( NuGetHelper.XNames.PackageSource ).ToList();
        var credentialsElement = root.Element( NuGetHelper.XNames.PackageSourceCredentials );
        foreach( var f in feeds )
        {
            bool mustEnsureSource = false;
            var s = addSources.FirstOrDefault( a => (string?)a.Attribute( NuGetHelper.XNames.Key ) == f.Name );
            if( s == null )
            {
                actions ??= [];
                actions.Add( $"Source '{f.Name}' is missing." );
                mustEnsureSource = true;
            }
            else
            {
                var url = (string?)s.Attribute( NuGetHelper.XNames.Value );
                if( url != f.Url )
                {
                    actions ??= [];
                    actions.Add( $"Source '{f.Name}' must reference '{f.Url}', not '{url}'." );
                    mustEnsureSource = true;
                }
                var m = mappings.FirstOrDefault( a => (string?)a.Attribute( NuGetHelper.XNames.Key ) == f.Name );
                if( m == null )
                {
                    actions ??= [];
                    actions.Add( $"Source mapping for '{f.Name}' is missing." );
                    mustEnsureSource = true;
                }
            }
            // Handling credentials: whether its the PublicReadCredentials or the "true" Credentials is
            // the same. The difference is that we must resolve the Credentials 
            var creds = f.PublicReadCredentials;
            if( creds == null && f.Credentials != null && withSecretCredentials != null )
            {
                var secret = withSecretCredentials.TryGetRequiredSecret( monitor, f.Credentials.SecretKey );
                if( secret == null )
                {
                    return false;
                }
                creds = new NuGetFeedCredentials( secret, "CKli" );
            }
            var credName = XNamespace.None + f.Name.Replace( " ", "_x0020_" );
            if( creds != null )
            {
                bool mustAdd = false;
                if( credentialsElement == null )
                {
                    actions ??= [];
                    actions.Add( $"Missing required <packageSourceCredentials> element." );
                    credentialsElement = new XElement( NuGetHelper.XNames.PackageSourceCredentials );
                    root.Add( credentialsElement );
                    actions.Add( $"Missing fake read credentials for '{f.Name}'." );
                    mustAdd = true;
                }
                else 
                {
                    var eCred = credentialsElement.Element( credName );
                    if( eCred == null )
                    {
                        if( f.PublicReadCredentials != null )
                        {
                            actions ??= [];
                            actions.Add( $"Missing public read credentials for '{f.Name}'." );
                        }
                        mustAdd = true;
                    }
                    else
                    {
                        bool hasUsername = false;
                        bool hasPwd = false;
                        foreach( var e in eCred.Elements( NuGetHelper.XNames.Add ) )
                        {
                            var key = (string?)e.Attribute( NuGetHelper.XNames.Key );
                            if( key == "Username" )
                            {
                                hasUsername = (string?)e.Attribute( NuGetHelper.XNames.Value ) == (creds.UserNameKey ?? "");
                            }
                            else if( key == "ClearTextPassword" )
                            {
                                hasPwd = (string?)e.Attribute( NuGetHelper.XNames.Value ) == creds.SecretKey;
                            }
                        }
                        if( !hasUsername || !hasPwd )
                        {
                            if( f.PublicReadCredentials != null )
                            {
                                actions ??= [];
                                actions.Add( $"Fake read credentials for '{f.Name}' must be updated." );
                            }
                            eCred.Elements( NuGetHelper.XNames.Add )
                                 .Where( a => (string?)a.Attribute( NuGetHelper.XNames.Key ) is "Username" or "ClearTextPassword" )
                                 .Remove();
                            mustAdd = true;
                        }
                    }
                }
                if( mustAdd )
                {
                    credentialsElement.Add( new XElement( credName,
                                              creds.ToNuGetUsernameElement(),
                                              creds.ToNuGetClearTextPasswordElement() ) );
                }
            }
            else if( credentialsElement != null )
            {
                var eCred = credentialsElement.Element( credName );
                if( eCred != null )
                {
                    actions ??= [];
                    actions.Add( $"Credentials for '{f.Name}' must be removed." );
                    eCred.Remove();
                }
            }

            if( mustEnsureSource )
            {
                NuGetHelper.SetOrRemoveNuGetSource( monitor, root, f.Name, f.Url );
            }
        }

        return true;
    }

    /// <summary>
    /// Gets the content of a <c>nuget.config</c> file based on the configured feeds
    /// (see <see cref="GetConfiguredNuGetFeeds(IActivityMonitor, out ImmutableArray{NuGetFeed})"/>).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>The NuGet configuration file content or null on error.</returns>
    public XDocument? GetDefaultNuGetConfig( IActivityMonitor monitor )
    {
        if( _defaultNugetConfig == null )
        {
            if( !GetConfiguredNuGetFeeds( monitor, out var feeds ) )
            {
                return null;
            }
            var root = new XElement( NuGetHelper.XNames.Configuration,
                            new XElement( NuGetHelper.XNames.PackageSources,
                                    new XElement( NuGetHelper.XNames.Clear ),
                                    feeds.Select( f => new XElement( NuGetHelper.XNames.Add,
                                                                        new XAttribute( NuGetHelper.XNames.Key, f.Name ),
                                                                        new XAttribute( NuGetHelper.XNames.Value, f.Url ) ) ) ),
                            new XElement( NuGetHelper.XNames.PackageSourceMapping,
                                    feeds.Select( f => new XElement( NuGetHelper.XNames.PackageSource,
                                                            new XAttribute( NuGetHelper.XNames.Key, f.Name ),
                                                            new XElement( NuGetHelper.XNames.Package,
                                                                    new XAttribute( NuGetHelper.XNames.Pattern, "*" ) ) ) ) ),
                            GetPackageSourceCredentials( feeds ) );

            static XElement? GetPackageSourceCredentials( ImmutableArray<NuGetFeed> feeds )
            {
                if( feeds.Any( f => f.PublicReadCredentials != null ) )
                {
                    return new XElement( NuGetHelper.XNames.PackageSourceCredentials,
                                         feeds.Where( f => f.PublicReadCredentials != null )
                                              .Select( f => new XElement( f.Name.Replace( " ", "_x0020_" ),
                                                                 f.PublicReadCredentials!.ToNuGetUsernameElement(),
                                                                 f.PublicReadCredentials!.ToNuGetClearTextPasswordElement() ) ) );
                }
                return null;
            }
            _defaultNugetConfig = new XDocument( root );
        }
        return _defaultNugetConfig;
    }

    /// <summary>
    /// Gets the local folder for assets.
    /// </summary>
    /// <param name="repo">The repository.</param>
    /// <param name="version">The version.</param>
    /// <returns>The assets local folder (may not exist).</returns>
    public NormalizedPath GetAssetsFolder( Repo repo, SVersion version )
    {
        return _localAssetsPath.AppendPart( repo.DisplayPath.LastPart ).AppendPart( version.ToString() );
    }

    /// <summary>
    /// <see cref="RepoArtifactInfo"/> factory.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="repo">The repository to consider.</param>
    /// <returns>The artifact information for the repository.</returns>
    protected override RepoArtifactInfo Create( IActivityMonitor monitor, Repo repo )
    {
        return new RepoArtifactInfo( this, repo );
    }

    /// <summary>
    /// Analyzes <see cref="LocalNuGetPath"/> to check whether all produced packages and files are locally available.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The released repository.</param>
    /// <param name="version">The released version.</param>
    /// <param name="buildContentInfo">The release info.</param>
    /// <param name="assetsFolder">
    /// Outputs the "$Local/&lt;world name&gt;/Assets/&lt;repo name&gt;/&lt;version&gt;" where artifacts are.
    /// This is <see cref="NormalizedPath.IsEmptyPath"/> if <see cref="BuildContentInfo.AssetFileNames"/> is empty.
    /// </param>
    /// <returns>True if the release's packages and asset files are locally available, false otherwise.</returns>
    public bool HasAllArtifacts( IActivityMonitor monitor,
                                 Repo repo,
                                 SVersion version,
                                 BuildContentInfo buildContentInfo,
                                 out NormalizedPath assetsFolder )
    {
        List<string>? missingPackages = null;
        List<string>? missingAssetFileNames = null;
        if( buildContentInfo.Produced.Length > 0 )
        {
            foreach( var p in buildContentInfo.Produced )
            {
                if( !File.Exists( Path.Combine( _localNuGetPath, $"{p}.{version}.nupkg" ) ) )
                {
                    missingPackages ??= new List<string>();
                    missingPackages.Add( p );
                }
            }
        }
        if( buildContentInfo.AssetFileNames.Length > 0 )
        {
            assetsFolder = GetAssetsFolder( repo, version );
            if( !Directory.Exists( assetsFolder ) )
            {
                missingAssetFileNames = [.. buildContentInfo.AssetFileNames];
            }
            else
            {
                foreach( var f in buildContentInfo.AssetFileNames )
                {
                    if( !File.Exists( assetsFolder.AppendPart( f ) ) )
                    {
                        missingAssetFileNames ??= new List<string>();
                        missingAssetFileNames.Add( f );
                    }

                }
            }
        }
        else
        {
            assetsFolder = default;
        }
        if( missingPackages == null && missingAssetFileNames == null )
        {
            return true;
        }
        if( missingPackages != null )
        {
            monitor.Info( $"""
            Missing {missingPackages.Count} local packages produced by '{repo.DisplayPath}' for version '{version}':
            {missingPackages.Concatenate()}
            """ );
        }
        if( missingAssetFileNames != null )
        {
            monitor.Info( $"""
            Missing {missingAssetFileNames.Count} asset files produced by '{repo.DisplayPath}' for version '{version}':
            {missingAssetFileNames.Concatenate()}
            """ );
        }
        return false;
    }

    /// <summary>
    /// This should only be called by the VersionTag plugin.
    /// <para>
    /// This is idempotent.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The source repository.</param>
    /// <param name="version">The release to destroy.</param>
    /// <param name="buildContentInfo">The build content.</param>
    /// <param name="removeFromNuGetGlobalCache">
    /// False to let the package in the NuGet global cache (if it exists).
    /// The global cache is "%userprofile%\.nuget\packages" on windows and "~/.nuget/packages" on Mac/Linux.
    /// </param>
    /// <returns>True on success, false if deleting some artifacts failed.</returns>
    public bool DestroyLocalRelease( IActivityMonitor monitor, Repo repo, SVersion version, BuildContentInfo buildContentInfo, bool removeFromNuGetGlobalCache = true )
    {
        using var _ = monitor.OpenInfo( $"""
            Removing produced artifacts of '{repo.DisplayPath}/v{version}':
            {buildContentInfo}
            """ );
        bool success = true;
        foreach( var p in buildContentInfo.Produced )
        {
            if( removeFromNuGetGlobalCache ) NuGetHelper.Cache.RemovePackage( monitor, p, version );
            success &= FileHelper.DeleteFile( monitor, Path.Combine( _localNuGetPath, $"{p}.{version}.nupkg" ) );
        }
        var assetsFolder = GetAssetsFolder( repo, version );
        success &= FileHelper.DeleteFolder( monitor, assetsFolder );
        return success;
    }

}
