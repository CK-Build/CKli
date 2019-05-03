using CK.Core;
using CSemVer;
using System;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Captures basic information that describes a package update for
    /// a primary or a secondary solution.
    /// </summary>
    public class UpdatePackageInfo
    {
        /// <summary>
        /// Initializes a new <see cref="UpdatePackageInfo"/>.
        /// </summary>
        /// <param name="solutionName">The solution name. Can not be null or white space.</param>
        /// <param name="projectName">The project name. Can not be null or white space.</param>
        /// <param name="package">The package identifier and version to upgrade.</param>
        public UpdatePackageInfo( string solutionName, string projectName, ArtifactInstance package )
        {
            if( String.IsNullOrWhiteSpace( solutionName ) ) throw new ArgumentNullException( nameof( solutionName ) );
            if( String.IsNullOrWhiteSpace( projectName ) ) throw new ArgumentNullException( nameof( projectName ) );
            SolutionName = solutionName;
            ProjectName = projectName;
            PackageUpdate = package;
        }

        /// <summary>
        /// Initializes a new <see cref="UpdatePackageInfo"/>.
        /// </summary>
        /// <param name="solutionName">The solution name. Can not be null or white space.</param>
        /// <param name="projectName">The project name. Can not be null or white space.</param>
        /// <param name="packageType">Type of the package.</param>
        /// <param name="packageId">The package identifier.</param>
        /// <param name="version">The package version.</param>
        public UpdatePackageInfo( string solutionName, string projectName, ArtifactType packageType, string packageId, SVersion version )
            : this( solutionName, projectName, new ArtifactInstance( packageType, packageId, version) )
        {
        }

        /// <summary>
        /// Initializes a new <see cref="UpdatePackageInfo"/>.
        /// </summary>
        /// <param name="e">Xml element. Must not be null.</param>
        /// <param name="solutionName">Solution name. Will be used if the element has no "Solution" attribute.</param>
        public UpdatePackageInfo( XElement e, string solutionName = null )
            : this( (string)e.Attribute("Solution") ?? solutionName,
                    (string)e.AttributeRequired("Project"),
                    new ArtifactInstance(
                        ArtifactType.Single( (string)e.Attribute( "PackageType" ) ),
                        (string)e.AttributeRequired( "PackageId" ),
                        SVersion.Parse( (string)e.AttributeRequired( "Version" )) ) )
        {
        }

        /// <summary>
        /// Gets the solution name that must be updated.
        /// Never null or white space.
        /// </summary>
        public string SolutionName { get; }

        /// <summary>
        /// Gets the project name that must be updated.
        /// Never null or white space.
        /// </summary>
        public string ProjectName { get; }

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
                        withSolutionName ? new XAttribute( "Solution", SolutionName ) : null,
                        new XAttribute( "Project", ProjectName ),
                        new XAttribute( "PackageType", PackageUpdate.Artifact.Type ),
                        new XAttribute( "PackageId", PackageUpdate.Artifact.Name ),
                        new XAttribute( "Version", PackageUpdate.Version ) );
        }
    }
}
