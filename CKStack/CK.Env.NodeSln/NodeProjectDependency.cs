using CK.Build;
using CK.Core;
using CSemVer;
using System.IO;

namespace CK.Env.NodeSln
{
    /// <summary>
    /// Captures a Node project dependency.
    /// </summary>
    public readonly struct NodeProjectDependency
    {
        /// <summary>
        /// Gets the name of the dependency.
        /// Can never be null or empty.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the type of the dependency.
        /// </summary>
        public NodeProjectDependencyType Type { get; }

        /// <summary>
        /// Gets the kind of the dependency (development, peer or transitive).
        /// </summary>
        public ArtifactDependencyKind Kind { get; }

        /// <summary>
        /// Gets the raw dependency as expressed in the package json file
        /// that can be of any <see cref="Type"/>.
        /// Can not be null.
        /// </summary>
        public string RawDep { get; }

        /// <summary>
        /// Gets the dependency version if <see cref="Type"/> is <see cref="NodeProjectDependencyType.VersionBound"/>
        /// or <see cref="NodeProjectDependencyType.LocalFeedTarball"/>, otherwise it is <see cref="SVersionBound.None"/>.
        /// <para>
        /// This version can be <see cref="SVersionLock.Lock"/>:
        /// <list type="bullet">
        ///     <item>For npm, [Lock] is the naked version: the '=' prefix is implied ("1.2.3" is like "=1.2.3").</item>
        ///     <item>"^1.2.3" is [LockMajor].</item>
        ///     <item>"~1.2.3" is [LockMinor].</item>
        ///     <item>">=1.2.3" is not locked (">1.2.3" is the same but <see cref="SVersionBound.ParseResult.IsApproximated"/> is true).</item>
        ///     <item>Other subtle rules apply. See <see cref="SVersionBound.NpmTryParse(System.ReadOnlySpan{char}, bool)"/> implementation for more information.</item>
        /// </list>
        /// </para>
        /// </summary>
        public SVersionBound Version { get; }


        public static NodeProjectDependency CreateFromVersion( string name, ArtifactDependencyKind kind, SVersion minVersion )
        {
            return new NodeProjectDependency( name, NodeProjectDependencyType.VersionBound, kind, ">=" + minVersion.ToString(), new SVersionBound( minVersion ) );
        }

        public static NodeProjectDependency CreateNPMDepLocalFeedTarball( NormalizedPath localFeedPath, string name, ArtifactDependencyKind kind, SVersion version )
        {
            return new NodeProjectDependency( name,
                                              NodeProjectDependencyType.LocalFeedTarball,
                                              kind,
                                              $"file:{CreateTarballPath( localFeedPath, name,version )}",
                                              new SVersionBound( version ) );
        }

        public static NormalizedPath CreateTarballPath( NormalizedPath localFeedPath, string name, SVersion version )
        {
            return localFeedPath.AppendPart( $"{name.Replace( "@", "" ).Replace( "/", "-" )}-{version.ToNormalizedString()}.tgz" );
        }

        public static NodeProjectDependency CreateLocalFeedTarballFromRawDep( string rawDep, string name, ArtifactDependencyKind kind, SVersion version )
        {
            return CreateNPMDepLocalFeedTarball( Path.GetDirectoryName( rawDep.Remove( 0, 5 ) ),
                                                 name,
                                                 kind,
                                                 version );
        }

        public NodeProjectDependency( string name, NodeProjectDependencyType type, ArtifactDependencyKind kind, string rawDep, SVersionBound version )
        {
            Name = name;
            Type = type;
            Kind = kind;
            RawDep = rawDep;
            Version = version;
        }

        public override string ToString() => $"{Kind.ToPackageJsonKey()}: {Name} => {RawDep}";

    }

}


