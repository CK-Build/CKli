using CK.Core;
using CodeCake.Abstractions;
using CSemVer;
using Kuinox.TypedCLI.NPM;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CodeCake
{
    public static class StandardGlobalInfoNPMExtension
    {
        /// <summary>
        /// Adds the <see cref="NPMSolution"/> to the <paramref name="globalInfo"/>
        /// </summary>
        /// <param name="this">This global info.</param>
        /// <param name="solution">The NPM solution.</param>
        /// <returns>This info.</returns>
        public static async Task AddNPM( this StandardGlobalInfo globalInfo, IActivityMonitor m, NPMSolution solution )
        {
            SVersion minmimalNpmVersionRequired = SVersion.Create( 6, 7, 0 );
            string? npmVersion = await Npm.Version( m );
            if( npmVersion is null ) throw new InvalidOperationException( "Could not fetch the npm version." );
            if( SVersion.Parse( npmVersion ) < minmimalNpmVersionRequired )
            {
                throw new InvalidOperationException( "Outdated npm. Version older than v6.7.0 are known to fail on publish." );
            }
            globalInfo.RegisterSolution( solution );
        }

        /// <summary>
        /// Adds the <see cref="Build.NPMArtifactType"/> for NPM based on <see cref="NPMSolution.ReadFromNPMSolutionFile"/>
        /// (projects are defined by "CodeCakeBuilder/NPMSolution.xml" file).
        /// </summary>
        /// <param name="this">This global info.</param>
        /// <returns>This info.</returns>
        public static Task AddNPM( this StandardGlobalInfo @this, IActivityMonitor m )
            => AddNPM( @this, m, NPMSolution.ReadFromNPMSolutionFile( m, @this ) );

        /// <summary>
        /// Gets the NPM solution handled by the single <see cref="Build.NPMArtifactType"/>.
        /// </summary>
        /// <param name="this">This global info.</param>
        /// <returns>The NPM solution.</returns>
        public static NPMSolution GetNPMSolution( this StandardGlobalInfo @this )
            => @this.Solutions.OfType<NPMSolution>().Single();

    }

    /// <summary>
    /// Encapsulates a set of <see cref="NPMProject"/> that can be <see cref="NPMPublishedProject"/>.
    /// </summary>
    public partial class NPMSolution : NPMProjectContainer, ISolution
    {
        readonly StandardGlobalInfo _globalInfo;

        /// <summary>
        /// Initiaizes a new <see cref="NPMSolution" />.
        /// </summary>
        /// <param name="projects">Set of projects.</param>
        NPMSolution(
            StandardGlobalInfo globalInfo )
            : base()
        {
            _globalInfo = globalInfo;
        }

        public IEnumerable<AngularWorkspace> AngularWorkspaces => Containers.OfType<AngularWorkspace>();


        public async Task<bool> RunInstall( IActivityMonitor m )
        {
            foreach( var p in SimpleProjects )
            {
                if( !await p.RunInstall( m ) ) return false;
            }

            foreach( var p in AngularWorkspaces )
            {
                if( !await p.WorkspaceProject.RunInstall( m ) ) return false;
            }
            return true;
        }

        public async Task<bool> Clean( IActivityMonitor m )
        {
            if( !await RunInstall( m ) ) return false;
            foreach( var p in SimpleProjects )
            {
                if( !await p.RunClean( m ) ) return false;
            }

            foreach( var p in AngularWorkspaces )
            {
                if( !await p.WorkspaceProject.RunClean( m ) ) return false;
            }
            return true;
        }



        /// <summary>
        /// Runs the 'build-debug', 'build-release' or 'build' script on all <see cref="SimpleProjects"/>.
        /// </summary>
        /// <param name="globalInfo">The global information object.</param>
        /// <param name="scriptMustExist">
        /// False to only emit a warning and return false if the script doesn't exist instead of
        /// throwing an exception.
        /// </param>
        public async Task<bool> Build( IActivityMonitor m )
        {
            foreach( var p in SimpleProjects )
            {
                if( !await p.RunBuild( m ) ) return false;
            }
            foreach( var p in AngularWorkspaces )
            {
                if( !await p.WorkspaceProject.RunBuild( m ) ) return false;
            }
            return true;
        }

        /// <summary>
        /// Runs the 'test' script on all <see cref="SimpleProjects"/>.
        /// </summary>
        /// <param name="globalInfo">The global information object.</param>
        /// <param name="globalInfo"></param>
        /// <param name="scriptMustExist">
        /// False to only emit a warning and return false if the script doesn't exist instead of
        /// throwing an exception.
        /// </param>
        public async Task<bool> Test( IActivityMonitor m )
        {
            foreach( var p in SimpleProjects )
            {
                if( !await p.RunTest( m ) ) return false;
            }
            foreach( var p in AngularWorkspaces )
            {
                if( !await p.WorkspaceProject.RunTest( m ) ) return false;
            }
            return true;
        }

        /// <summary>
        /// Generates the .tgz file in the <see cref="StandardGlobalInfo.ReleasesFolder"/>
        /// by calling npm pack for all <see cref="SimplePublishedProjects"/>.
        /// </summary>
        /// <param name="globalInfo">The global information object.</param>
        /// <param name="cleanupPackageJson">
        /// By default, "scripts" and "devDependencies" are removed from the package.json file.
        /// </param>
        /// <param name="packageJsonPreProcessor">Optional package.json pre processor.</param>
        public async Task<bool> RunPack( IActivityMonitor m, Action<JObject>? packageJsonPreProcessor = null )
        {
            foreach( var p in AllPublishedProjects )
            {
                bool res = await p.RunPack( m, packageJsonPreProcessor );
                if( !res ) return false;
            }
            return true;
        }

        /// <summary>
        /// Reads the "CodeCakeBuilder/NPMSolution.xml" file that must exist.
        /// </summary>
        /// <param name="version">The version of all published packages.</param>
        /// <returns>The solution object.</returns>
        public static NPMSolution ReadFromNPMSolutionFile( IActivityMonitor m, StandardGlobalInfo globalInfo )
        {
            var document = XDocument.Load( "CodeCakeBuilder/NPMSolution.xml" ).Root;
            var solution = new NPMSolution( globalInfo );

            foreach( var item in document.Elements( "AngularWorkspace" ) )
            {
                solution.Add( AngularWorkspace.Create( m, globalInfo,
                         solution,
                         (string)item.Attribute( "Path" ) ) );
            }
            foreach( var item in document.Elements( "Project" ) )
            {
                solution.Add( NPMPublishedProject.Create( m,
                        globalInfo,
                        solution,
                        (string)item.Attribute( "Path" ),
                        (string)item.Attribute( "OutputFolder" ) ) );
            }
            return solution;
        }
    }
}
