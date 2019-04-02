using CK.Core;
using CK.Text;
using System;

namespace CK.Env
{
    /// <summary>
    /// Encapsulates component definition.
    /// </summary>
    public readonly struct CKSetupComponent : IEquatable<CKSetupComponent>
    {
        /// <summary>
        /// Initializes a new <see cref="CKSetupComponent"/>.
        /// </summary>
        /// <param name="projectPath">The project folder path (relative to the solution folder).</param>
        /// <param name="targetFramework">The atomic target framework.</param>
        public CKSetupComponent( NormalizedPath projectPath, CKTrait targetFramework )
        {
            ProjectPath = projectPath;
            TargetFramework = targetFramework.ToString();
            FullName = projectPath.AppendPart( TargetFramework );
        }

        /// <summary>
        /// Initializes a new <see cref="CKSetupComponent"/>.
        /// </summary>
        /// <param name="pathAndFramework">The project folder path (relative to the solution folder) and the target framework.</param>
        public CKSetupComponent( NormalizedPath pathAndFramework )
        {
            FullName = pathAndFramework;
            ProjectPath = pathAndFramework.RemoveLastPart();
            TargetFramework = pathAndFramework.LastPart;
        }

        /// <summary>
        /// Gets the project folder.
        /// </summary>
        public NormalizedPath ProjectPath { get; }

        /// <summary>
        /// Gets the name of the component (folder name).
        /// </summary>
        public string Name => ProjectPath.LastPart;

        /// <summary>
        /// Gest the target framework folder name: "net461", "netcoreapp2.0", "netstandard2.0", etc.
        /// </summary>
        public string TargetFramework { get; }

        /// <summary>
        /// Gets this CKSetupComponent as a <see cref="GeneratedArtifact"/>.
        /// </summary>
        public Artifact GeneratedArtifact => new Artifact( "CKSetup", Name + '/' + TargetFramework );

        /// <summary>
        /// Gets the full name.
        /// </summary>
        public NormalizedPath FullName { get; }

        /// <summary>
        /// Overridden to return <see cref="FullName"/>: <see cref="ProjectPath"/>/<see cref="TargetFramework"/>.
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => FullName;

        /// <summary>
        /// Get the bin path (relative to the solution folder).
        /// </summary>
        /// <param name="buildConfiguration">Build configuration (Debug/Release).</param>
        /// <returns>The bin path.</returns>
        public string GetBinPath( string buildConfiguration ) => $"{ProjectPath}/bin/{buildConfiguration}/{TargetFramework}";

        /// <summary>
        /// Equality is <see cref="FullName"/> based.
        /// </summary>
        /// <param name="other">Other to chanllenge.</param>
        /// <returns>True when equals, false otherwise.</returns>
        public bool Equals( CKSetupComponent other ) => FullName == other.FullName;

        public override bool Equals( object obj ) => obj is CKSetupComponent o ? Equals( o ) : false;

        public override int GetHashCode() => FullName.GetHashCode();
    }
}
