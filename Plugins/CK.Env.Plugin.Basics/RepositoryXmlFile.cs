using CK.Core;
using CK.Text;
using System;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env.Plugin
{
    public class RepositoryXmlFile : XmlFilePluginBase, IDisposable, IGitBranchPlugin, ICommandMethodsProvider
    {
        static readonly XNamespace SVGNS = XNamespace.Get( "http://csemver.org/schemas/2015" );
        static readonly XName xRootName = SVGNS + "RepositoryInfo";
        static readonly XName xBranchesName = SVGNS + "Branches";
        static readonly XName xDebugName = SVGNS + "Debug";
        static readonly XName XBranchName = SVGNS + "Branch";

        public readonly SolutionDriver _driver;
        XElement _branches;

        public RepositoryXmlFile( GitFolder f, NormalizedPath branchPath, SolutionDriver driver )
            : base( f, branchPath, branchPath.AppendPart( "RepositoryInfo.xml" ), null )
        {
            if( PluginBranch == StandardGitStatus.Local )
            {
                f.OnLocalBranchEntered += OnLocalBranchEntered;
                f.OnLocalBranchLeaving += OnLocalBranchLeaving;
            }
            _driver = driver;
            _driver.OnStartBuild += OnStartBuild;
            _driver.OnEndBuild += OnEndBuild;
        }

        void OnStartBuild( object sender, BuildStartEventArgs e )
        {
            var debug = EnsureDocument().Root.Element( xDebugName );
            if( debug == null )
            {
                EnsureDocument().Root.Add( debug = new XElement( xDebugName ) );
                e.Memory.Add( this, debug );
            }
            if( (bool?)debug.Attribute( "IgnoreDirtyWorkingFolder" ) != true )
            {
                debug.SetAttributeValue( "IgnoreDirtyWorkingFolder", "true" );
                e.Memory[this] = null;
            }
            Save( e.Monitor );
        }

        void OnEndBuild( object sender, BuildEndEventArgs e )
        {
            if( !e.BuildStartArgs.IsUsingDirtyFolder
                && e.BuildStartArgs.Memory.TryGetValue( this, out object mark ) )
            {
                if( mark != null ) ((XElement)mark).Remove();
                else EnsureDocument().Root.Element( xDebugName ).SetAttributeValue( "IgnoreDirtyWorkingFolder", "false" );
                Save( e.Monitor );
            }
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FilePath;

        void OnLocalBranchEntered( object sender, EventMonitoredArgs e )
        {
            EnsureLocalBranch( e.Monitor );
            Save( e.Monitor );
        }

        void OnLocalBranchLeaving( object sender, EventMonitoredArgs e )
        {
            RemoveLocalBranch( e.Monitor );
            Save( e.Monitor );
        }

        void IDisposable.Dispose()
        {
            GitFolder.OnLocalBranchEntered -= OnLocalBranchEntered;
            GitFolder.OnLocalBranchLeaving -= OnLocalBranchLeaving;
            _driver.OnStartBuild -= OnStartBuild;
            _driver.OnEndBuild -= OnEndBuild;
        }

        public bool CanApplySettings => GitFolder.CurrentBranchName == BranchPath.LastPart;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( !this.CheckCurrentBranch( m ) ) return;
            EnsureBranchMapping( m, GitFolder.World.DevelopBranchName, "develop" );
            if( PluginBranch == StandardGitStatus.Local )
            {
                EnsureLocalBranch( m );
            }
            else
            {
                RemoveLocalBranch( m );
            }
            // If the <Debug > element exists, we set the IgnoreDirtyWorkingFolder attribute to false.
            EnsureDocument().Root.Element( xDebugName )?.SetAttributeValue( "IgnoreDirtyWorkingFolder", "false" );
            // Obsolete:
            Document.Root.Elements( SVGNS + "PossibleVersionsMode" ).Remove();
            Save( m );
        }

        /// <summary>
        /// Ensures that the <see cref="XmlFilePluginBase.Document"/> exists.
        /// </summary>
        /// <returns>The xml document.</returns>
        public XDocument EnsureDocument() => Document ?? (Document = new XDocument( new XElement( xRootName ) ));

        /// <summary>
        /// Ensures that Branches element exists in the non null <see cref="XmlFilePluginBase.Document"/>.
        /// If the Document is null, this is null.
        /// </summary>
        public XElement Branches => _branches ?? (_branches = Document?.Root.EnsureElement( xBranchesName ));

        /// <summary>
        /// Ensures that document and the branches element exists.
        /// </summary>
        public XElement EnsureBranches()
        {
            EnsureDocument();
            return Branches;
        }

        /// <summary>
        /// Ensures that the document exists and the specified branch mapping exists.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="branchName">The actual branch name (ex: "develop-local").</param>
        /// <param name="versionName">The package suffix (ex: "local").</param>
        /// <param name="isZeroTimed">True for SimpleGitVersion ci version mode for "ZeroTimed" instead of "LastReleaseBased".</param>
        void EnsureBranchMapping( IActivityMonitor m, string branchName, string versionName, bool isZeroTimed = false )
        {
            var branches = EnsureBranches();

            var branch = branches.Elements( XBranchName )
                                 .FirstOrDefault( b => (string)b.Attribute( "Name" ) == branchName );
            if( branch == null )
            {
                branches.Add( branch = new XElement( XBranchName,
                                            new XAttribute( "Name", branchName ) ) );
            }
            branch.SetAttributeValue( "VersionName", versionName );
            branch.SetAttributeValue( "CIVersionMode", isZeroTimed ? "ZeroTimed" : "LastReleaseBased" );
        }

        /// <summary>
        /// Removes the branch mapping if it exists.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="branchName">The actual branch name (ex: "develop-local").</param>
        void RemoveBranchMapping( IActivityMonitor m, string branchName )
        {
            Document?.Root.Element( xBranchesName )
                             .Elements( XBranchName )
                             .Where( b => (string)b.Attribute( "Name" ) == branchName )
                             .Remove();
        }

        /// <summary>
        /// Ensures that the document and the local branch mapping exists:
        /// <see cref="IWorldName.LocalBranchName"/> is mapped to "local".
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        void EnsureLocalBranch( IActivityMonitor m ) => EnsureBranchMapping( m, GitFolder.World.LocalBranchName, "local", true );

        /// <summary>
        /// Removes the <see cref="IWorldName.LocalBranchName"/> branch mapping if it exists.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        void RemoveLocalBranch( IActivityMonitor m ) => RemoveBranchMapping( m, GitFolder.World.LocalBranchName );

    }
}
