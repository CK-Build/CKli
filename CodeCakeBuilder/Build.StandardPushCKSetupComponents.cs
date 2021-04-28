using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeCake
{
    public partial class Build
    {
        /// <summary>
        /// Gets the components that this solution produces.
        /// (This is extracted as an independent function to be more easily transformable.)
        /// </summary>
        /// <returns>The set of components to export.</returns>
        public static IEnumerable<CKSetupComponent> GetCKSetupComponents()
        {
            return new CKSetupComponent[]{
new CKSetupComponent( "ProjectFolder", "TargetFramework" )
};
        }

        /// <summary>
        /// Encapsulates component definition.
        /// </summary>
        public readonly struct CKSetupComponent
        {
            /// <summary>
            /// Initializes a new <see cref="CKSetupComponent"/>.
            /// </summary>
            /// <param name="projectPath">The project folder path (relative to the solution folder).</param>
            /// <param name="targetFramework">The target framework folder name: "net461", "netcoreapp2.0", "netstandard2.0", etc.</param>
            public CKSetupComponent( string projectPath, string targetFramework )
            {
                ProjectPath = projectPath;
                TargetFramework = targetFramework;
            }

            /// <summary>
            /// Gets the project folder.
            /// </summary>
            public string ProjectPath { get; }

            /// <summary>
            /// Gets the name of the component (folder name).
            /// </summary>
            public string Name => Path.GetFileName( ProjectPath );

            /// <summary>
            /// Gest the target framework folder name: "net461", "netcoreapp2.0", "netstandard2.0", etc.
            /// </summary>
            public string TargetFramework { get; }

            public override string ToString() => Name + '/' + TargetFramework;

            /// <summary>
            /// Get the bin path.
            /// </summary>
            /// <param name="buildConfiguration">Build configuration (Debug/Release).</param>
            /// <returns>The bin path.</returns>
            public string GetBinPath( string buildConfiguration ) => $"{ProjectPath}/bin/{buildConfiguration}/{TargetFramework}";
        }
    }
}
