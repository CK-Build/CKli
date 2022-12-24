using CK.Core;
using CSemVer;
using CK.Build;
using System.Xml.Linq;

namespace CK.Env.MSBuildSln
{
    public class DeclaredPackageDependency
    {
        static readonly ArtifactType _nugetType = ArtifactType.Single( "NuGet" );

        internal DeclaredPackageDependency( Project owner,
                                            string packageId,
                                            SVersionBound version,
                                            XElement originElement,
                                            XElement finalVersionElement,
                                            CKTrait frameworks,
                                            string privateAsset,
                                            bool isVersionOverride )
        {
            Owner = owner;
            PackageId = packageId;
            Version = version;
            OriginElement = originElement;
            FinalVersionElement = finalVersionElement;
            Frameworks = frameworks;
            PrivateAsset = privateAsset;
            IsVersionOverride = isVersionOverride;
        }

        /// <summary>
        /// Gets the project that owns this dependency.
        /// </summary>
        public Project Owner { get; }

        /// <summary>
        /// Gets the package identifier.
        /// </summary>
        public string PackageId { get; }

        /// <summary>
        /// Gets the <see cref="ArtifactInstance"/> based on the "NuGet" artifact type, the <see cref="PackageId"/> and the <see cref="SVersionBound.Base"/> version.
        /// </summary>
        public ArtifactInstance BaseArtifactInstance => new ArtifactInstance( _nugetType, PackageId, Version.Base );

        /// <summary>
        /// Gets the version.
        /// This version can be <see cref="SVersionLock.Lock"/>:
        /// <list type="bullet">
        ///     <item>For NuGet, this is an "Exact version match" denoted by brackets: "[1.2.3]".</item>
        ///     <item>For npm, this is the naked version: the '=' prefix is implied ("1.2.3" is like "=1.2.3").</item>
        /// </list>
        /// Locked versions are generally NOT updated by CKli.
        /// </summary>
        public SVersionBound Version { get; }

        /// <summary>
        /// Gets the frameworks to which this dependency applies.
        /// When no Condition apply, this is the full <see cref="MSProject.TargetFrameworks"/>.
        /// </summary>
        public CKTrait Frameworks { get; }

        /// <summary>
        /// Gets the PackageReference element.
        /// </summary>
        public XElement OriginElement { get; }

        /// <summary>
        /// Gets the element that defines the $(Version) if a property version is used (The element is like &lt;CKCoreVersion&gt;13.0.1&lt;/CKCoreVersion&gt;)
        /// or is the &lt;PackageReference Update="PackageName" Version="..." /&lt; centrally managed package
        /// (see https://github.com/microsoft/MSBuildSdks/tree/master/src/CentralPackageVersions).
        /// When null, the <see cref="OriginElement"/> must be used (and the version is either in Version or VersionOverride attribute).
        /// </summary>
        public XElement FinalVersionElement { get; }

        /// <summary>
        /// Gets whether VersionOverride is used locally in the Project file whereas packages
        /// are centrally managed: https://github.com/microsoft/MSBuildSdks/tree/master/src/CentralPackageVersions.
        /// </summary>
        public bool IsVersionOverride { get; }

        /// <summary>
        /// Gets the "PrivateAsset" value: when "all", the PackageReference model Kind is set to ArtifactDependencyKind.Private
        /// instead of Transitive.
        /// </summary>
        public string PrivateAsset { get; }

        /// <summary>
        /// Overridden to return the <see cref="Owner"/> => <see cref="PackageId"/>/<see cref="Version"/> (<see cref="Frameworks"/>).
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => $"{Owner} => {PackageId}/{Version} ({Frameworks})";
    }
}
