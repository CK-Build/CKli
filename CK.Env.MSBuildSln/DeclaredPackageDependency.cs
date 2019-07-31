using CK.Core;
using CSemVer;
using System.Xml.Linq;

namespace CK.Env.MSBuildSln
{
    public class DeclaredPackageDependency
    {
        internal DeclaredPackageDependency(
            Project owner,
            string packageId,
            bool versionLocked,
            SVersion version,
            XElement originElement,
            XElement finalDeclaration,
            CKTrait frameworks )
        {
            Owner = owner;
            Package = new ArtifactInstance( ArtifactType.Single( "NuGet" ), packageId, version );
            OriginElement = originElement;
            PropertyVersionElement = finalDeclaration;
            Frameworks = frameworks;
            VersionLocked = versionLocked;
        }

        /// <summary>
        /// Gets the project that owns this dependency.
        /// </summary>
        public Project Owner { get; }

        /// <summary>
        /// Gets the package identifier and version.
        /// </summary>
        public ArtifactInstance Package { get; }

        /// <summary>
        /// Gets the package identifier.
        /// </summary>
        public string PackageId => Package.Artifact.Name;

        /// <summary>
        /// Gets whether the version is locked: it is inside square brackets (ex. Version="[2.6.4]").
        /// </summary>
        public bool VersionLocked { get; }

        /// <summary>
        /// Gets the version.
        /// </summary>
        public SVersion Version => Package.Version;

        /// <summary>
        /// Gets the frameworks to which this dependency applies.
        /// </summary>
        public CKTrait Frameworks { get; }

        /// <summary>
        /// Gets the PackageReference element.
        /// </summary>
        public XElement OriginElement { get; }

        /// <summary>
        /// Gets the element that defines the $(Version) if a property version is used. Null otherwise.
        /// </summary>
        public XElement PropertyVersionElement { get; }

        /// <summary>
        /// Gets whether the version use a $(Version) property.
        /// </summary>
        public bool UsePropertyVersion => PropertyVersionElement != null;

        /// <summary>
        /// Overridden to return the <see cref="Owner"/> => <see cref="PackageId"/>/<see cref="Version"/> (<see cref="Frameworks"/>).
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => $"{Owner} => {PackageId}/{Version} ({Frameworks})";
    }
}
