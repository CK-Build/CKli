using CK.Env.DependencyModel;
using CSemVer;
using System;

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
        public ProjectDependencyKind Kind { get; }

        /// <summary>
        /// Gets the raw dependency as expressed in the package json file
        /// that can be of any <see cref="Type"/>.
        /// Can not be null.
        /// </summary>
        public string RawDep { get; }

        /// <summary>
        /// Gets the dependency version if <see cref="Type"/>
        /// is <see cref="NPMVersionDependencyType.MinVersion"/>, otherwise it is null.
        /// </summary>
        public SVersion MinVersion { get; }

        public NPMDep( string name, ProjectDependencyKind kind, SVersion minVersion )
        {
            Name = name;
            Type = NPMVersionDependencyType.MinVersion;
            Kind = kind;
            MinVersion = minVersion ?? throw new ArgumentNullException( nameof( minVersion ) );
            RawDep = ">=" + minVersion.ToNuGetPackageString();
        }

        public NPMDep( string name, NPMVersionDependencyType type, ProjectDependencyKind kind, string rawDep, SVersion minVersion )
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
