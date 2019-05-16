using CK.Core;
using CK.Env.DependencyModel;
using CSemVer;
using System;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Captures basic information that describes a package update for a project.
    /// </summary>
    public class UpdatePackageInfo
    {
        /// <summary>
        /// Initializes a new <see cref="UpdatePackageInfo"/>.
        /// </summary>
        /// <param name="project">The project. Can not be null.</param>
        /// <param name="package">The package identifier and version to upgrade.</param>
        public UpdatePackageInfo( IProject project, ArtifactInstance package )
        {
            Project = project;
            PackageUpdate = package;
        }

        /// <summary>
        /// Initializes a new <see cref="UpdatePackageInfo"/>.
        /// </summary>
        /// <param name="project">The project. Can not be null.</param>
        /// <param name="packageType">Type of the package.</param>
        /// <param name="packageId">The package identifier.</param>
        /// <param name="version">The package version.</param>
        public UpdatePackageInfo( IProject project, ArtifactType packageType, string packageId, SVersion version )
            : this( project, new ArtifactInstance( packageType, packageId, version) )
        {
        }

        ///// <summary>
        ///// Initializes a new <see cref="UpdatePackageInfo"/>.
        ///// </summary>
        ///// <param name="e">Xml element. Must not be null.</param>
        ///// <param name="solutionName">Solution name. Will be used if the element has no "Solution" attribute.</param>
        //public UpdatePackageInfo( XElement e, string solutionName = null )
        //    : this( (string)e.Attribute("Solution") ?? solutionName,
        //            (string)e.AttributeRequired("Project"),
        //            new ArtifactInstance(
        //                ArtifactType.Single( (string)e.Attribute( "PackageType" ) ),
        //                (string)e.AttributeRequired( "PackageId" ),
        //                SVersion.Parse( (string)e.AttributeRequired( "Version" )) ) )
        //{
        //}

        /// <summary>
        /// Gets the project that must be updated.
        /// </summary>
        public IProject Project { get; }

        /// <summary>
        /// Gets the package to update and its target version.
        /// </summary>
        public ArtifactInstance PackageUpdate { get; }

        /// <summary>
        /// Exports this <see cref="UpdatePackageInfo"/> in Xml format.
        /// </summary>
        /// <param name="withSolutionName">When true, adds the "Solution" attribute.</param>
        /// <returns>The Xml.</returns>
        public XElement ToXml( bool withSolutionName )
        {
            return new XElement( "PackageUdate",
                        withSolutionName ? new XAttribute( "Solution", Project.Solution.Name ) : null,
                        new XAttribute( "Project", Project.SimpleProjectName+'|'+Project.Type+'|'+Project.SolutionRelativeFolderPath ),
                        new XAttribute( "PackageType", PackageUpdate.Artifact.Type ),
                        new XAttribute( "PackageId", PackageUpdate.Artifact.Name ),
                        new XAttribute( "Version", PackageUpdate.Version ) );
        }
    }
}
