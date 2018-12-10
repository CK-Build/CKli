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
    /// Defines build context: it contains everything that is required to actually build a solution.
    /// The only mutable information here is the <see cref="EnvironmentVariables"/>.
    /// </summary>
    public class BuildStartEventArgs : EventMonitoredArgs
    {
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
        public bool IsUsingDirtyFolder => (BuildType & BuildType.IsUsingDirtyFolder) != 0;

        /// <summary>
        /// Gets whether the build uses a Zero builder.
        /// </summary>
        public bool WithZeroBuilder => (BuildType & BuildType.WithZeroBuilder) == BuildType.WithZeroBuilder;

        /// <summary>
        /// Gets the physical path to the solution folder.
        /// </summary>
        public string SolutionPhysicalPath { get; }

        /// <summary>
        /// Gets the physical path CodeCakeBuilder executable file path.
        /// </summary>
        public string CodeCakeBuilderExecutableFile { get; }

        /// <summary>
        /// Gets whether the Code Cake Builder should be compiled first or the existing <see cref="CodeCakeBuilderExecutableFile"/>
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
            string solutionPhysicalPath,
            string codeCakeBuilderExecutableFile,
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
            SolutionPhysicalPath = solutionPhysicalPath;
            BuildCodeCakeBuilderIsRequired = buildCodeCakeBuilder;
            CodeCakeBuilderExecutableFile = codeCakeBuilderExecutableFile;
        }
    }
}
