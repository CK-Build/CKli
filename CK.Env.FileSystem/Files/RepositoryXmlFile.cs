using CK.Core;
using System;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env
{
    public class RepositoryXmlFile : GitFolderXmlFile
    {
        static readonly XNamespace SVGNS = XNamespace.Get( "http://csemver.org/schemas/2015" );
        static readonly XName XRootName = SVGNS + "RepositoryInfo";
        static readonly XName XBranchesName = SVGNS + "Branches";
        static readonly XName XDebugName = SVGNS + "Debug";
        static readonly XName XBranchName = SVGNS + "Branch";

        XElement _branches;

        public RepositoryXmlFile( GitFolder f )
            : base( f, "RepositoryInfo.xml" )
        {
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
        /// Ensures that the document and the local branch mapping exists.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        void EnsureLocalBranch( IActivityMonitor m )
        {
            var branches = EnsureBranches();

            var branch = branches.Elements( XBranchName )
                                 .FirstOrDefault( b => (string)b.Attribute( "Name" ) == Folder.World.LocalBranchName );
            if( branch == null )
            {
                branches.Add( branch = new XElement( XBranchesName,
                                            new XAttribute( "Name", Folder.World.LocalBranchName ) ) );
            }
            branch.SetAttributeValue( "VersionName", "local" );
            branch.SetAttributeValue( "CIVersionMode", "LastReleaseBased" );
        }

        /// <summary>
        /// Removes the local branch mapping if it exists.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        void RemoveLocalBranch( IActivityMonitor m )
        {
            Document?.Root.Element( XBranchesName )
                     .Elements( XBranchName )
                     .Where( b => (string)b.Attribute( "Name" ) == Folder.World.LocalBranchName )
                     .Remove();
        }
    }
}
