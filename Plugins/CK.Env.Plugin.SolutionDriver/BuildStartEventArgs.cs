using CK.Core;
using CK.Env.DependencyModel;
using CSemVer;
using System;
using System.Collections.Generic;

namespace CK.Env.Plugin
{
    /// <summary>
    /// Defines build context: it contains everything that is required to actually build a solution.
    /// The only mutable information here is the <see cref="EnvironmentVariables"/> and the .
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
        /// Gets the solution.
        /// </summary>
        public ISolution Solution { get; }

        /// <summary>
        /// Gets whether tests must be executed.
        /// </summary>
        public bool WithUnitTest => (BuildType & BuildType.WithUnitTests) != 0;

        /// <summary>
        /// Gets a mutable list of environment variables.
        /// </summary>
        public readonly IList<(string KeyName, string Value)> EnvironmentVariables;

        /// <summary>
        /// Gets the <see cref="BuildType"/>.
        /// </summary>
        public BuildType BuildType { get; }

        /// <summary>
        /// Gets whether the build uses a dirty folder: a <see cref="GitFolder.ResetHard(IActivityMonitor)"/> will be executed after
        /// the build to restore the solution folder.
        /// </summary>
        public bool IsUsingDirtyFolder => (BuildType & BuildType.IsUsingDirtyFolder) != 0;

        /// <summary>
        /// Gets whether the build uses the Zero builder (direct call of the existing <see cref="CodeCakeBuilderExecutableFile"/>).
        /// When false, CodeCakeBuilder project is compiled and run (with 'dotnet run').
        /// </summary>
        public bool WithZeroBuilder => (BuildType & BuildType.WithZeroBuilder) == BuildType.WithZeroBuilder;

        /// <summary>
        /// Gets whether the builder must push the artifacts to the remotes.
        /// </summary>
        public bool WithPushToRemote => (BuildType & BuildType.WithPushToRemote) != 0;

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

        /// <summary>
        /// Gets a bag of objects that enables state sharing between participants and from <see cref="BuildStartEventArgs"/>
        /// to <see cref="BuildEndEventArgs"/>.
        /// </summary>
        public IDictionary<object, object> Memory { get; }

        internal BuildStartEventArgs(
            IActivityMonitor m,
            bool buildRequired,
            ISolution solution,
            SVersion version,
            BuildType buildType,
            string solutionPhysicalPath,
            string codeCakeBuilderExecutableFile )
            : base( m )
        {
            if( !buildRequired && (buildType & (BuildType.WithUnitTests | BuildType.WithPushToRemote)) == 0 ) throw new ArgumentException( "No build, tests or push." );

            BuildIsRequired = buildRequired;
            Solution = solution ?? throw new ArgumentNullException( nameof( solution ) );
            Version = version ?? throw new ArgumentNullException( nameof( version ) );
            EnvironmentVariables = new List<(string KeyName, string Value)>();
            BuildType = buildType;
            SolutionPhysicalPath = solutionPhysicalPath;
            CodeCakeBuilderExecutableFile = codeCakeBuilderExecutableFile;
            Memory = new Dictionary<object, object>();
        }
    }
}
