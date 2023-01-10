using Cake.Npm;
using CK.Core;
using CodeCake.Abstractions;
using CSemVer;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public static StandardGlobalInfo AddNPM( this StandardGlobalInfo globalInfo, NPMSolution solution )
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

        [Obsolete("Simply use GetNodeSolution() instead.", true )]
        public static StandardGlobalInfo AddNPM( this StandardGlobalInfo @this )
        {
            return null!;
        }

        [Obsolete( "Simply use GetNodeSolution() instead.", true )]
        public static StandardGlobalInfo AddYarn( this StandardGlobalInfo @this )
        {
            return null!;
        }

        /// <summary>
        /// Gets the single NPM solution.
        /// </summary>
        /// <param name="this">This global info.</param>
        /// <returns>The NPM solution.</returns>
        [Obsolete( "Call GetNodeSolution() instead.", true )]
        public static NPMSolution GetNPMSolution( this StandardGlobalInfo @this )
        {
            return GetNodeSolution( @this );
        }

        [Obsolete( "Call GetNodeSolution() instead.", true )]
        public static NPMSolution GetYarnSolution( this StandardGlobalInfo @this )
        {
            return GetNodeSolution( @this );
        }

        /// <summary>
        /// Gets the Node solution.
        /// </summary>
        /// <param name="this">This global info.</param>
        /// <returns>The Node solution.</returns>
        public static NPMSolution GetNodeSolution( this StandardGlobalInfo @this )
        {
            var s = @this.Solutions.OfType<NPMSolution>().SingleOrDefault();
            if( s == null )
            {
                SVersion minmimalNpmVersionRequired = SVersion.Create( 6, 7, 0 );
                string npmVersion = @this.Cake.NpmGetNpmVersion();
                if( SVersion.Parse( npmVersion ) < minmimalNpmVersionRequired )
                {
                    @this.Cake.TerminateWithError( "Outdated npm. Version older than v6.7.0 are known to fail on publish." );
                }
                @this.RegisterSolution( s );
            }
            return s;
        }

    }

    /// <summary>
    /// Encapsulates a set of <see cref="NPMProject"/> that can be <see cref="NPMPublishedProject"/>.
    /// </summary>
    public partial class NPMSolution : NPMProjectContainer, ICIWorkflow
    {

        /// <summary>
        /// Initializes a new <see cref="NPMSolution" />.
        /// </summary>
        /// <param name="projects">Set of projects.</param>
        NPMSolution( StandardGlobalInfo globalInfo )
        {
            GlobalInfo = globalInfo;
        }

        public IEnumerable<AngularWorkspace> AngularWorkspaces => Containers.OfType<AngularWorkspace>();

        public StandardGlobalInfo GlobalInfo { get; }

        public void RunNpmCI()
        {
            foreach( var p in SimpleProjects )
            {
                p.RunNpmCi();
            }

            foreach( var p in AngularWorkspaces )
            {
                p.WorkspaceProject.RunNpmCi();
            }
        }

        public void Clean()
        {
            RunNpmCI();

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
        /// <param name="packageJsonPreProcessor">Optional package.json preprocessor.</param>
        public void RunPack( Action<JObject> packageJsonPreProcessor = null )
        {
            foreach( var p in AllPublishedProjects )
            {
                p.RunPack( packageJsonPreProcessor );
            }
        }

        /// <summary>
        /// Reads the RepositoryInfo.xml" &lt;NodeSolution&gt; that must exist.
        /// </summary>
        /// <param name="globalInfo">The global information.</param>
        /// <param name="useYarn">True to use Yarn instead of NPM.</param>
        /// <returns>The solution object.</returns>
        public static NPMSolution ReadFromRepositoryInfo( StandardGlobalInfo globalInfo )
        {
            var document = XDocument.Load( "RepositoryInfo.xml" ).Root.Element( "NodeSolution" );
            var solution = new NPMSolution( globalInfo );
            foreach( var item in document.Elements() )
            {
                var path = new NormalizedPath( (string?)item.Attribute( "Path" ) );
                if( path.IsEmptyPath )
                {
                    Throw.InvalidOperationException( $"RepositoryInfo.xml: <NodeSolution> element {item} requires a Path non empty attribute." );
                }
                bool useYarn = FindYarnFolder( path );
                if( item.Name.LocalName == "AngularWorkspace" )
                {
                    solution.Add( AngularWorkspace.Create( solution, path, useYarn ) );
                }
                else if( item.Name.LocalName == "NodeProject" )
                {
                    solution.Add( NPMPublishedProject.Create( solution,
                                                              path,
                                                              (string?)item.Attribute( "OutputPath" ),
                                                              useYarn ) );
                }
                else if( item.Name.LocalName == "YarnWorkspace" )
                {
                    Throw.NotSupportedException( $"RepositoryInfo.xml: YarnWorkspace is not supported yet (where are the projects output paths?)." );
                }
                else
                {
                    Throw.InvalidOperationException( $"RepositoryInfo.xml: <NodeSolution> unhandled element {item}." );
                }
            }
            return solution;
        }

        static bool FindYarnFolder( NormalizedPath path )
        {
            var yarnPath = path;
            while( !yarnPath.IsEmptyPath )
            {
                if( Directory.Exists( yarnPath.AppendPart( ".yarn" ) ) ) return true;
                yarnPath = yarnPath.RemoveLastPart();
            }
            return false;
        }
    }
}
