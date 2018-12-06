using CK.Core;
using CK.Env.MSBuild;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    /// <summary>
    /// Defines build context.
    /// The only mutable information here is the <see cref="EnvironmentVariables"/>.
    /// </summary>
    public class BuildStartEventArgs : EventMonitoredArgs
    {
        public static readonly string CodeCakeBuilderRelativePath = "CodeCakeBuilder/bin/Debug/netcoreapp2.1/CodeCakeBuilder.dll";

        /// <summary>
        /// Gets the version that must be generated.
        /// </summary>
        public SVersion Version { get; }

        /// <summary>
        /// Gets whether a build is required.
        /// When false, <see cref="WithUnitTest"/> is necessarily true.
        /// </summary>
        public bool BuildIsRequired { get; }

        /// <summary>
        /// Gets the primary solution.
        /// </summary>
        public Solution PrimarySolution { get; }

        /// <summary>
        /// Gets whether tests must be executed.
        /// </summary>
        public bool WithUnitTest { get; }

        /// <summary>
        /// Gets a mutable list of environment variables.
        /// </summary>
        public readonly IList<(string KeyName, string Value)> EnvironmentVariables;

        /// <summary>
        /// Gets the <see cref="BuildType"/>.
        /// </summary>
        public BuildType BuildType { get; }

        /// <summary>
        /// Gets whether the build uses a dirty folder.
        /// </summary>
        public bool IsUsingDirtyFolder => (BuildType & CK.Env.BuildType.IsUsingDirtyFolder) != 0;

        /// <summary>
        /// Gets the physical path to the solution folder.
        /// </summary>
        public string SolutionFolderPhysicalPath { get; }

        /// <summary>
        /// Gets the physical path to the solution's Code Cake Builder executable.
        /// </summary>
        public string CodeCakeBuilderExecutablePhysicalPath { get; }

        /// <summary>
        /// Gets whether the Code Cake Builder should be compiled first or the existing <see cref="CodeCakeBuilderExecutablePhysicalPath"/>
        /// be directly called.
        /// </summary>
        public bool BuildCodeCakeBuilderIsRequired { get; }

        public BuildStartEventArgs(
            IActivityMonitor m,
            bool buildRequired,
            Solution primary,
            bool withUnitTest,
            SVersion version,
            BuildType buildType,
            bool buildCodeCakeBuilder )
            : base( m )
        {
            if( primary == null ) throw new ArgumentNullException( nameof( primary ) );
            if( version == null ) throw new ArgumentNullException( nameof( version ) );
            if( !buildRequired && !withUnitTest ) throw new ArgumentException( "No build nor tests." );

            BuildIsRequired = buildRequired;
            PrimarySolution = primary;
            Version = version;
            WithUnitTest = withUnitTest;
            EnvironmentVariables = new List<(string KeyName, string Value)>();
            BuildType = buildType;

            SolutionFolderPhysicalPath = primary.GitFolder.FileSystem.GetFileInfo( primary.SolutionFolderPath ).PhysicalPath;
            CodeCakeBuilderExecutablePhysicalPath = System.IO.Path.Combine( SolutionFolderPhysicalPath, CodeCakeBuilderRelativePath );

            BuildCodeCakeBuilderIsRequired = buildCodeCakeBuilder || !System.IO.File.Exists( CodeCakeBuilderExecutablePhysicalPath );

        }
    }
}
