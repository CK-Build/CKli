using CK.Build;
using CK.Text;
using CSemVer;
using System.IO;

namespace CK.Env.Plugin
{
    public readonly struct NPMDep
    {
        /// <summary>
        /// Gets the name of the dependency.
        /// Can never be null or empty.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the type of the dependency.
        /// </summary>
        public NPMVersionDependencyType Type { get; }

        /// <summary>
        /// Gets the kind of the dependency (dev, peer, etc.).
        /// </summary>
        public ArtifactDependencyKind Kind { get; }

        /// <summary>
        /// Gets the raw dependency as expressed in the package json file
        /// that can be of any <see cref="Type"/>.
        /// Can not be null.
        /// </summary>
        public string RawDep { get; }

        /// <summary>
        /// Gets the dependency version if <see cref="Type"/>
        /// is <see cref="NPMVersionDependencyType.MinVersion"/> or <see cref="NPMVersionDependencyType.LocalFeedTarball"/>, otherwise it is null.
        /// </summary>
        public SVersion MinVersion { get; }

        public static NPMDep CreateNPMDepMinVersion( string name, ArtifactDependencyKind kind, SVersion minVersion )
        {
            return new NPMDep( name, NPMVersionDependencyType.MinVersion, kind, ">=" + minVersion.ToString(), minVersion );
        }

        public static NPMDep CreateNPMDepLocalPath( NormalizedPath path, string name, ArtifactDependencyKind kind )
        {
            return new NPMDep( name, NPMVersionDependencyType.LocalPath, kind, $"file:{path}", null );
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="dirPath">The dir path must exist.</param>
        /// <param name="name"></param>
        /// <param name="kind"></param>
        /// <returns></returns>
        public static NPMDep CreateNPMDepLocalFeedTarball( NormalizedPath dirPath, string name, ArtifactDependencyKind kind, SVersion version )
        {
            string fileName = $"{name.Replace( "@", "" ).Replace( "/", "-" )}-{version.ToNormalizedString()}.tgz";
            return new NPMDep( name,
                NPMVersionDependencyType.LocalFeedTarball,
                kind,
                $"file:{ dirPath.AppendPart( fileName ).ToString()}",
                version );
        }
        public static NPMDep CreateNPMDepLocalFeedTarballFromRawDep( string rawDep, string name, ArtifactDependencyKind kind, SVersion version )
        {
            return CreateNPMDepLocalFeedTarball(
                Path.GetDirectoryName( rawDep.Remove( 0, 5 ) ),
                name,
                kind,
                version
                );
        }

        internal static SVersion GetVersionOutOfTarballPath( string path, string packageName )
        {
            path = Path.GetFileNameWithoutExtension( path );
            path = path.Remove( packageName.Length + 1 ); //remove the package name
            return SVersion.TryParse( path );
        }

        public NPMDep( string name, NPMVersionDependencyType type, ArtifactDependencyKind kind, string rawDep, SVersion minVersion )
        {
            Name = name;
            Type = type;
            Kind = kind;
            RawDep = rawDep;
            MinVersion = minVersion;
        }

        public override string ToString() => $"{Kind.ToPackageJsonKey()}: {Name} => {RawDep}";

    }
}
