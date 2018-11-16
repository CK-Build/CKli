using CK.Core;
using CK.Text;
using System;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env
{
    public class RepositoryXmlFile : GitFolderXmlFile, IGitBranchPlugin, ICommandMethodsProvider
    {
        static readonly XNamespace SVGNS = XNamespace.Get( "http://csemver.org/schemas/2015" );
        static readonly XName XRootName = SVGNS + "RepositoryInfo";
        static readonly XName XBranchesName = SVGNS + "Branches";
        static readonly XName XDebugName = SVGNS + "Debug";
        static readonly XName XBranchName = SVGNS + "Branch";

        XElement _branches;

        public RepositoryXmlFile( GitFolder f, NormalizedPath branchPath )
            : base( f, branchPath.AppendPart( "RepositoryInfo.xml" ) )
        {
            BranchPath = branchPath;
        }

        public NormalizedPath BranchPath { get; }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FilePath;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( !this.CheckCurrentBranch( m ) ) return;
            EnsureBranchMapping( m, Folder.World.DevelopBranchName, "develop" );
            if( Folder.StandardGitStatus == StandardGitStatus.LocalBranch )
            {
                EnsureLocalBranch( m );
            }
            else
            {
                RemoveLocalBranch( m );
            }
            // Obsolete:
            Document.Root.Elements( SVGNS+"PossibleVersionsMode" ).Remove();
            Save( m );
        }

        /// <summary>
        /// Ensures that the <see cref="GitFolderXmlFile.Document"/> exists.
        /// </summary>
        /// <returns>The xml document.</returns>
        public XDocument EnsureDocument() => Document ?? (Document = new XDocument( new XElement( XRootName ) ));

        /// <summary>
        /// Ensures that Branches element is the first element of the non null <see cref="GitFolderXmlFile.Document"/>.
        /// If the Document is null, this is null.
        /// </summary>
        public XElement Branches => _branches ?? (_branches = Document?.Root.EnsureFirstElement( XBranchesName ) );

        /// <summary>
        /// Ensures that document and the branches element exists.
        /// </summary>
        public XElement EnsureBranches()
        {
            EnsureDocument();
            return Branches;
        }

        /// <summary>
        /// Sets the IgnoreDirtyWorkingFolder to true.
        /// There is no associated remove/clear since this is used locally
        /// by the builder that calls git reset once done. 
        /// </summary>
        public void SetIgnoreDirtyFolders()
        {
            EnsureDocument().Root.EnsureElement( XDebugName ).SetAttributeValue( "IgnoreDirtyWorkingFolder", true );
        }

        /// <summary>
        /// Ensures that the document exists and the specified branch mapping exists.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="branchName">The actual branch name (ex: "develop-local").</param>
        /// <param name="versionName">The package suffix (ex: "local").</param>
        void EnsureBranchMapping( IActivityMonitor m, string branchName, string versionName )
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
            branch.SetAttributeValue( "CIVersionMode", "LastReleaseBased" );
        }

        /// <summary>
        /// Removes the branch mapping if it exists.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="branchName">The actual branch name (ex: "develop-local").</param>
        void RemoveBranchMapping( IActivityMonitor m, string branchName )
        {
            Document?.Root.Element( XBranchesName )
                     .Elements( XBranchName )
                     .Where( b => (string)b.Attribute( "Name" ) == branchName )
                     .Remove();
        }

        /// <summary>
        /// Ensures that the document and the local branch mapping exists:
        /// <see cref="IWorldName.LocalBranchName"/> is mapped to "local".
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        void EnsureLocalBranch( IActivityMonitor m ) => EnsureBranchMapping( m, Folder.World.LocalBranchName, "local" );

        /// <summary>
        /// Removes the <see cref="IWorldName.LocalBranchName"/> branch mapping if it exists.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        void RemoveLocalBranch( IActivityMonitor m ) => RemoveBranchMapping( m, Folder.World.LocalBranchName );

    }
}
