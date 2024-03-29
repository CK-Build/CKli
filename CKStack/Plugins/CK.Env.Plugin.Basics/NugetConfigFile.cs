using CK.Core;
using CK.Env.MSBuildSln;
using CK.SimpleKeyVault;
using System;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env.Plugin
{
    public class NugetConfigFile : XmlFilePluginBase, IDisposable, IGitBranchPlugin, ICommandMethodsProvider
    {
        readonly SolutionSpec _solutionSpec;
        readonly IEnvLocalFeedProvider _localFeedProvider;
        readonly SolutionDriver _solutionDriver;
        readonly SecretKeyStore _secretStore;
        XElement _packageSources;

        public NugetConfigFile( GitRepository f,
                                SolutionDriver driver,
                                IEnvLocalFeedProvider localFeedProvider,
                                SecretKeyStore secretStore,
                                SolutionSpec s,
                                NormalizedPath branchPath )
            : base( f, branchPath, branchPath.AppendPart( "NuGet.config" ), null )
        {
            _localFeedProvider = localFeedProvider;
            _solutionSpec = s;
            _solutionDriver = driver;
            _secretStore = secretStore;
            if( IsOnLocalBranch )
            {
                f.OnLocalBranchEntered += OnLocalBranchEntered;
                f.OnLocalBranchLeaving += OnLocalBranchLeaving;
            }
            _solutionDriver.OnStartBuild += OnStartBuild;
            _solutionDriver.OnZeroBuildProject += OnZeroBuildProject;
            _solutionDriver.OnSolutionConfiguration += OnSolutionConfiguration;
        }

        void OnZeroBuildProject( object? sender, ZeroBuildEventArgs e )
        {
            if( e.IsStarting )
            {
                EnsureFeed( e.Monitor, "ZeroBuild-Feed", _localFeedProvider.ZeroBuild.PhysicalPath );
                Save( e.Monitor, true );
            }
            // Since we ResetHard, we don't need to restore the file.
            // else RemoveFeed( e.Monitor, "ZeroBuild-Feed" );
        }

        void OnStartBuild( object? sender, BuildStartEventArgs e )
        {
            if( !e.IsUsingDirtyFolder ) return;

            if( (e.BuildType & BuildType.IsTargetCI) != 0 ) EnsureLocalFeeds( e.Monitor, ensureDevelop: true );
            else if( (e.BuildType & BuildType.IsTargetRelease) != 0 ) EnsureLocalFeeds( e.Monitor, ensureRelease: true );
            else if( (e.BuildType & BuildType.IsTargetLocal) == 0 ) throw new ArgumentException( nameof( BuildType ) );
            Save( e.Monitor );
        }

        void OnLocalBranchEntered( object? sender, EventMonitoredArgs e )
        {
            EnsureLocalFeeds( e.Monitor, true, true, true );
            Save( e.Monitor );
        }

        void OnLocalBranchLeaving( object? sender, EventMonitoredArgs e )
        {
            RemoveLocalFeeds( e.Monitor );
            Save( e.Monitor );
        }

        void OnSolutionConfiguration( object? sender, SolutionConfigurationEventArgs e )
        {
            // These values are not build secrets. They are required by ApplySettings to configure
            // the NuGet.config file: once done, restore can be made and having these keys available
            // as environment variables will not help.
            var creds = e.Solution.ArtifactSources.OfType<INuGetFeed>()
                            .Where( s => s.Credentials != null && s.Credentials.IsSecretKeyName )
                            .Select( s => s.Credentials!.PasswordOrSecretKeyName );
            foreach( var c in creds )
            {
                _secretStore.DeclareSecretKey( c!, current => current?.Description ?? "Needed to configure NuGet.config file." );
            }
        }

        void IDisposable.Dispose()
        {
            GitFolder.OnLocalBranchEntered -= OnLocalBranchEntered;
            GitFolder.OnLocalBranchLeaving -= OnLocalBranchLeaving;
            _solutionDriver.OnSolutionConfiguration -= OnSolutionConfiguration;
            _solutionDriver.OnStartBuild -= OnStartBuild;
            _solutionDriver.OnZeroBuildProject -= OnZeroBuildProject;
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FilePath;

        public bool IsOnLocalBranch => BranchPath.LastPart == GitFolder.World.LocalBranchName;

        public bool CanApplySettings => GitFolder.CurrentBranchName == BranchPath.LastPart;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor monitor )
        {
            if( !this.CheckCurrentBranch( monitor ) ) return;

            var solution = _solutionDriver.GetSolution( monitor, allowInvalidSolution: true );
            if( solution == null ) return;

            // Update the "Solution Items" folder.
            solution.Tag<SolutionFile>()?.EnsureSolutionItemFile( monitor, FilePath.RemovePrefix( BranchPath ) );

            EnsureDocument();
            PackageSources.EnsureFirstElement( "clear" );
            foreach( var s in solution.ArtifactSources.OfType<INuGetFeed>() )
            {
                EnsureFeed( monitor, s.Name, s.Url );
                if( s.Credentials != null )
                {
                    string? password = s.Credentials.IsSecretKeyName
                                        ? _secretStore.GetSecretKey( monitor, s.Credentials.PasswordOrSecretKeyName, throwOnUnavailable: false )
                                        : s.Credentials.PasswordOrSecretKeyName;
                    if( password != null )
                    {
                        EnsureFeedCredentials( monitor, s.Name, s.Credentials.UserName, password );
                    }
                    else
                    {
                        if( s.Credentials.IsSecretKeyName )
                            monitor.Warn( $"Secret '{s.Credentials.PasswordOrSecretKeyName}' is not known. Configuration for feed '{s.Name}' skipped." );
                        else monitor.Warn( $"Empty feed password. Configuration for feed '{s.Name}' skipped." );
                    }
                }
                else
                {
                    DeleteFeedCredentials( monitor, s.Name );
                }
            }
            var packages = EnsureDocument().Root!.Element( "packageSourceCredentials" );
            if( packages != null && !packages.Nodes().Any() )
            {
                packages.Remove();
            }
            foreach( var name in _solutionSpec.RemoveNuGetSourceNames )
            {
                RemoveFeed( monitor, name, withCredentials: true );
            }
            // Cleanup if ever needed.
            RemoveFeed( monitor, "ZeroBuild-Feed" );
            if( IsOnLocalBranch )
            {
                EnsureLocalFeeds( monitor );
            }
            else
            {
                RemoveLocalFeeds( monitor );
            }
            Save( monitor );
        }

        /// <summary>
        /// Ensures that the <see cref="XmlFilePluginBase.Document"/> exists.
        /// </summary>
        /// <returns>The xml document.</returns>
        public XDocument EnsureDocument() => Document ?? (Document = new XDocument( new XElement( "configuration" ) ));

        /// <summary>
        /// Ensures that packageSources element is the first element of the non null <see cref="XmlFilePluginBase.Document"/>.
        /// If the Document is null, this is null.
        /// </summary>
        public XElement PackageSources => _packageSources ?? (_packageSources = Document?.Root.EnsureFirstElement( "packageSources" ));

        /// <summary>
        /// Ensures that packageSources element is the first element of the <see cref="XmlFilePluginBase.Document"/>.
        /// <see cref="EnsureDocument()"/> is called.
        /// </summary>
        public XElement EnsurePackageSources()
        {
            EnsureDocument();
            return PackageSources;
        }

        /// <summary>
        /// Ensures that a feed identified by its name is a given path or url.
        /// The document is created if it does not exist.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="feedName">The name of the feed. This is the key.</param>
        /// <param name="urlOrPath">The NuGet source.</param>
        /// <returns>Returns true on success, false on error.</returns>
        public void EnsureFeed( IActivityMonitor m, string feedName, string urlOrPath )
        {
            if( String.IsNullOrWhiteSpace( feedName ) ) throw new ArgumentNullException( nameof( feedName ) );
            if( String.IsNullOrWhiteSpace( urlOrPath ) ) throw new ArgumentNullException( nameof( urlOrPath ) );
            EnsurePackageSources().EnsureAddKeyValue( feedName, urlOrPath );
        }

        /// <summary>
        /// Ensures that credential section exists and a feed has a given credential.
        /// The document is created if it does not exist.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="feedName">The nameof the feed.</param>
        /// <param name="urlOrPath">The NuGet source.</param>
        public void EnsureFeedCredentials( IActivityMonitor m, string feedName, string userName, string clearTextPassword )
        {
            if( String.IsNullOrWhiteSpace( feedName ) ) throw new ArgumentNullException( nameof( feedName ) );
            if( String.IsNullOrWhiteSpace( userName ) ) throw new ArgumentNullException( nameof( userName ) );
            if( String.IsNullOrWhiteSpace( clearTextPassword ) ) throw new ArgumentNullException( nameof( clearTextPassword ) );

            var rootCred = EnsureDocument().Root.EnsureElement( "packageSourceCredentials" );
            var safeName = feedName.Replace( " ", "_x0020_" );
            var entry = rootCred.EnsureElement( safeName );
            entry.EnsureAddKeyValue( "Username", userName );
            entry.EnsureAddKeyValue( "ClearTextPassword", clearTextPassword );
        }

        public void DeleteFeedCredentials( IActivityMonitor m, string feedName )
        {
            if( String.IsNullOrWhiteSpace( feedName ) ) throw new ArgumentNullException( nameof( feedName ) );
            var rootCred = EnsureDocument().Root.EnsureElement( "packageSourceCredentials" );
            var element = rootCred.Element( feedName );
            if( element != null )
            {
                m.Info( $"Removing Credentials of feed {feedName}." );
                element.Remove();
            }
        }

        /// <summary>
        /// Updates the NuGet config file with LocalFeed/Local, LocalFeed/CI and/or LocalFeed/Release
        /// sources.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="ensureLocal">True to add the LocalFeed/Local.</param>
        /// <param name="ensureDevelop">True to add the LocalFeed/Develop.</param>
        /// <param name="ensureRelease">True to add the LocalFeed/Release.</param>
        public void EnsureLocalFeeds( IActivityMonitor m, bool ensureLocal = false, bool ensureDevelop = false, bool ensureRelease = false )
        {
            if( ensureLocal )
            {
                EnsureFeed( m, "LocalFeed-Local", _localFeedProvider.Local.PhysicalPath );
            }
            if( ensureDevelop )
            {
                EnsureFeed( m, "LocalFeed-Develop", _localFeedProvider.CI.PhysicalPath );
            }
            if( ensureRelease )
            {
                EnsureFeed( m, "LocalFeed-Master", _localFeedProvider.Release.PhysicalPath );
            }
        }

        /// <summary>
        /// Removes a source by its feed name.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="feedName">The feed name.</param>
        /// <param name="withCredentials">True to remove credentials.</param>
        public void RemoveFeed( IActivityMonitor m, string feedName, bool withCredentials = true )
        {
            if( PackageSources != null )
            {
                _packageSources
                        .Elements( "add" )
                        .FirstOrDefault( b => (string)b.Attribute( "key" ) == feedName )
                        ?.ClearCommentsBeforeAndNewLineAfter().Remove();
                if( withCredentials ) RemoveFeedCredential( m, feedName );
            }
        }

        /// <summary>
        /// Removes a source by its feed name.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="feedName">The feed name.</param>
        public void RemoveFeedCredential( IActivityMonitor m, string feedName )
        {
            Document?.Root.Element( "packageSourceCredentials" )
                         ?.Element( feedName.Replace( " ", "_x0020_" ) )
                         ?.ClearCommentsBeforeAndNewLineAfter().Remove();
        }


        /// <summary>
        /// Removes any local sources.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        public void RemoveLocalFeeds( IActivityMonitor m )
        {
            RemoveFeed( m, "LocalFeed-Local", false );
            RemoveFeed( m, "LocalFeed-Develop", false );
            RemoveFeed( m, "LocalFeed-Master", false );
        }
    }
}
