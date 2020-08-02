using Cake.Npm;
using CodeCake.Abstractions;
using CSemVer;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CodeCake
{
    public static class StandardGlobalInfoNPMExtension
    {
        /// <summary>
        /// Adds the <see cref="NodeSolution"/> to the <paramref name="globalInfo"/>
        /// </summary>
        /// <param name="this">This global info.</param>
        /// <param name="solution">The NPM solution.</param>
        /// <returns>This info.</returns>
        public static StandardGlobalInfo AddNode( this StandardGlobalInfo globalInfo, NodeSolution solution )
        {
            SVersion minmimalNpmVersionRequired = SVersion.Create( 6, 7, 0 );
            string npmVersion = globalInfo.Cake.NpmGetNpmVersion();
            if( SVersion.Parse( npmVersion ) < minmimalNpmVersionRequired )
            {
                globalInfo.Cake.TerminateWithError( "Outdated npm. Version older than v6.7.0 are known to fail on publish." );
            }
            globalInfo.RegisterSolution( solution );
            return globalInfo;
        }

        /// <summary>
        /// Adds the <see cref="Build.NPMArtifactType"/> for NPM based on <see cref="NodeSolution.ReadFromNPMSolutionFile"/>
        /// (projects are defined by "CodeCakeBuilder/NPMSolution.xml" file).
        /// </summary>
        /// <param name="this">This global info.</param>
        /// <returns>This info.</returns>
        public static StandardGlobalInfo AddNode( this StandardGlobalInfo @this )
        {
            return AddNode( @this, NodeSolution.ReadFromNPMSolutionFile( @this ) );
        }

        /// <summary>
        /// Gets the NPM solution handled by the single <see cref="Build.NPMArtifactType"/>.
        /// </summary>
        /// <param name="this">This global info.</param>
        /// <returns>The NPM solution.</returns>
        public static NodeSolution GetNodeSolution( this StandardGlobalInfo @this )
        {
            return @this.Solutions.OfType<NodeSolution>().Single();
        }

    }

    /// <summary>
    /// Encapsulates a set of <see cref="YarnWorkspace"/> that can be <see cref="NPMPublishedProject"/>.
    /// </summary>
    public partial class NodeSolution : NPMProjectContainer, ICIWorkflow
    {
        readonly StandardGlobalInfo _globalInfo;

        /// <summary>
        /// Initiaizes a new <see cref="NodeSolution" />.
        /// </summary>
        /// <param name="projects">Set of projects.</param>
        NodeSolution(
            StandardGlobalInfo globalInfo )
            : base()
        {
            _globalInfo = globalInfo;
        }

        public IEnumerable<AngularWorkspace> AngularWorkspaces => Containers.OfType<AngularWorkspace>();


        public void Install()
        {
            foreach( var p in SimpleProjects )
            {
                p.Install();
            }

            foreach( var p in AngularWorkspaces )
            {
                p.WorkspaceProject.Install();
            }
        }

        public void Clean()
        {
            Install();

            foreach( var p in SimpleProjects )
            {
                p.RunClean();
            }

            foreach( var p in AngularWorkspaces )
            {
                p.WorkspaceProject.RunClean();
            }
        }



        /// <summary>
        /// Runs the 'build-debug', 'build-release' or 'build' script on all <see cref="SimpleProjects"/>.
        /// </summary>
        /// <param name="globalInfo">The global information object.</param>
        /// <param name="scriptMustExist">
        /// False to only emit a warning and return false if the script doesn't exist instead of
        /// throwing an exception.
        /// </param>
        public void Build()
        {
            foreach( var p in SimpleProjects )
            {
                p.RunBuild();
            }
            foreach( var p in AngularWorkspaces )
            {
                p.WorkspaceProject.RunBuild();
            }
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
        public void Test()
        {
            foreach( var p in SimpleProjects )
            {
                p.RunTest();
            }
            foreach( var p in AngularWorkspaces )
            {
                p.WorkspaceProject.RunTest();
            }
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
        public void RunPack( Action<JObject> packageJsonPreProcessor = null )
        {
            foreach( var p in AllPublishedProjects )
            {
                p.RunPack( packageJsonPreProcessor );
            }
        }

        /// <summary>
        /// Reads the "CodeCakeBuilder/NPMSolution.xml" file that must exist.
        /// </summary>
        /// <param name="version">The version of all published packages.</param>
        /// <returns>The solution object.</returns>
        public static NodeSolution ReadFromNPMSolutionFile( StandardGlobalInfo globalInfo )
        {
            var document = XDocument.Load( "CodeCakeBuilder/NPMSolution.xml" ).Root;
            var solution = new NodeSolution(globalInfo);

            foreach( var item in document.Elements( "AngularWorkspace" ) )
            {
                solution.Add( AngularWorkspace.Create( globalInfo,
                         solution,
                         (string)item.Attribute( "Path" ) ) );
            }
            foreach( var item in document.Elements( "Project" ) )
            {
                solution.Add( NPMPublishedProject.Create(
                        globalInfo,
                        solution,
                        (string)item.Attribute( "Path" ),
                        (string)item.Attribute( "OutputFolder" ) ) );
            }
            return solution;
        }
    }
}
