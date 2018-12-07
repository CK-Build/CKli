using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env.Plugins.SolutionFiles
{
    public class NugetConfigFile : XmlFilePluginBase, IDisposable, IGitBranchPlugin, ICommandMethodsProvider
    {
        readonly ISolutionSettings _settings;
        readonly IEnvLocalFeedProvider _localFeedProvider;
        readonly SolutionDriver _solutionDriver;
        XElement _packageSources;

        public NugetConfigFile( GitFolder f, SolutionDriver driver, IEnvLocalFeedProvider localFeedProvider, ISolutionSettings s, NormalizedPath branchPath )
            : base( f, branchPath, branchPath.AppendPart( "NuGet.config" ) )
        {
            _localFeedProvider = localFeedProvider;
            _settings = s;
            _solutionDriver = driver;
            if( IsOnLocalBranch )
            {
                f.OnLocalBranchEntered += OnLocalBranchEntered;
                f.OnLocalBranchLeaving += OnLocalBranchLeaving;
            }
            // On local branch, the NuGet.config already has the LocalFeed/Local source.
            if( PluginBranch != StandardGitStatus.Local )
            {
                _solutionDriver.OnZeroBuildProject += OnZeroBuildProject;
            }
            _solutionDriver.OnStartBuild += OnStartBuild;
        }

        void OnZeroBuildProject( object sender, EventMonitoredArgs e )
        {
            Debug.Assert( PluginBranch != StandardGitStatus.Local );
            EnsureLocalFeeds( e.Monitor, false, ensureDevelop: true );
            Save( e.Monitor );
        }

        void OnStartBuild( object sender, BuildStartEventArgs e )
        {
            // Building on 'local' and 'remote develop' uses the CodeCakeBuilder as-is.
            if( e.BuildType == BuildType.Local || e.BuildType != BuildType.DevelopWithRemotes ) return;

            Debug.Assert( (e.BuildType & BuildType.IsTargetLocal) == 0 );
            Debug.Assert( e.IsUsingDirtyFolder );

            if( (e.BuildType & BuildType.IsTargetDevelop) != 0 ) EnsureLocalFeeds( e.Monitor, false, true );
            else if( (e.BuildType & BuildType.IsTargetRelease) != 0 ) EnsureLocalFeeds( e.Monitor, true );
            else throw new ArgumentException( nameof( BuildType ) );
            Save( e.Monitor );
        }

        void OnLocalBranchEntered( object sender, EventMonitoredArgs e )
        {
            EnsureLocalFeeds( e.Monitor );
            Save( e.Monitor );
        }

        void OnLocalBranchLeaving( object sender, EventMonitoredArgs e )
        {
            RemoveLocalFeeds( e.Monitor );
            Save( e.Monitor );
        }

        void IDisposable.Dispose()
        {
            Folder.OnLocalBranchEntered -= OnLocalBranchEntered;
            Folder.OnLocalBranchLeaving -= OnLocalBranchLeaving;
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FilePath;

        public bool IsOnLocalBranch => BranchPath.LastPart == Folder.World.LocalBranchName;

        public bool CanApplySettings => Folder.CurrentBranchName == BranchPath.LastPart;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( !this.CheckCurrentBranch( m ) ) return;
            EnsureDocument();
            PackageSources.EnsureFirstElement( "clear" );
            foreach( var s in _settings.NuGetSources )
            {
                EnsureFeed( m, s.Name, s.Url );
                if( s.Credentials != null )
                {
                    EnsureFeedCredentials( m, s.Name, s.Credentials.UserName, s.Credentials.Password );
                }
            }
            foreach( var name in _settings.ExcludedNuGetSourceNames )
            {
                RemoveFeed( m, name, withCredentials: true );
            }
            if( IsOnLocalBranch )
            {
                EnsureLocalFeeds( m );
            }
            else
            {
                RemoveLocalFeeds( m );
            }
            Save( m );
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

        /// <summary>
        /// Updates the NuGet config file with LocalFeed/Local, LocalFeed/CI and/or LocalFeed/Release
        /// sources.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="ensureLocal">True to add the LocalFeed/Local.</param>
        /// <param name="ensureDevelop">True to add the LocalFeed/Develop.</param>
        /// <param name="ensureRelease">True to add the LocalFeed/Release.</param>
        public void EnsureLocalFeeds( IActivityMonitor m, bool ensureLocal = true, bool ensureDevelop = true, bool ensureRelease = true )
        {
            if( ensureLocal )
            {
                EnsureFeed( m, "LocalFeed-Local", _localFeedProvider.Local.PhysicalPath );
            }
            if( ensureDevelop )
            {
                EnsureFeed( m, "LocalFeed-Develop", _localFeedProvider.Develop.PhysicalPath );
            }
            if( ensureRelease )
            {
                EnsureFeed( m, "LocalFeed-Master", _localFeedProvider.Master.PhysicalPath );
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
