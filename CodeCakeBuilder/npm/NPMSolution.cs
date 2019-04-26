using Cake.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CodeCake
{
    /// <summary>
    /// Encapsulates a set of <see cref="NPMProject"/> that can be <see cref="NPMPublishedProject"/>.
    /// </summary>
    public class NPMSolution
    {
        /// <summary>
        /// Initiaizes a new <see cref="NPMSolution" />.
        /// </summary>
        /// <param name="projects">Set of projects.</param>
        public NPMSolution( IEnumerable<NPMProject> projects )
        {
            Projects = projects.ToArray();
            PublishedProjects = Projects.OfType<NPMPublishedProject>().ToArray();
        }

        /// <summary>
        /// Gets all the NPM projects.
        /// </summary>
        public IReadOnlyList<NPMProject> Projects { get; }

        /// <summary>
        /// Gets the published projects.
        /// </summary>
        public IReadOnlyList<NPMPublishedProject> PublishedProjects { get; }

        /// <summary>
        /// Runs "npm -i" on all <see cref="Projects"/>.
        /// </summary>
        /// <param name="globalInfo">The global information object.</param>
        public void RunInstall( StandardGlobalInfo globalInfo )
        {
            foreach( var p in Projects )
            {
                p.RunInstall( globalInfo );
            }
        }

        /// <summary>
        /// Runs "npm -i"  and a required clean script on all <see cref="Projects"/>.
        /// </summary>
        /// <param name="cleanScriptName">The script name that must exist in the package.json.</param>
        /// <param name="scriptMustExist">
        /// False to only emit a warning and return false if the script doesn't exist instead of
        /// throwing an exception.
        /// </param>
        public void RunInstallAndClean( StandardGlobalInfo globalInfo, string cleanScriptName = "clean", bool scriptMustExist = true )
        {
            foreach( var p in Projects )
            {
                p.RunInstallAndClean( globalInfo, cleanScriptName );
            }
        }

        /// <summary>
        /// Runs the 'build-debug', 'build-release' or 'build' script on all <see cref="Projects"/>.
        /// </summary>
        /// <param name="globalInfo">The global information object.</param>
        /// <param name="scriptMustExist">
        /// False to only emit a warning and return false if the script doesn't exist instead of
        /// throwing an exception.
        /// </param>
        public void RunBuild( StandardGlobalInfo globalInfo, bool scriptMustExist = true )
        {
            foreach( var p in Projects )
            {
                p.RunBuild( globalInfo, scriptMustExist );
            }
        }

        /// <summary>
        /// Runs the 'test' script on all <see cref="Projects"/>.
        /// </summary>
        /// <param name="globalInfo">The global information object.</param>
        /// <param name="globalInfo"></param>
        /// <param name="scriptMustExist">
        /// False to only emit a warning and return false if the script doesn't exist instead of
        /// throwing an exception.
        /// </param>
        public void RunTest( StandardGlobalInfo globalInfo, bool scriptMustExist = true )
        {
            foreach( var p in Projects )
            {
                p.RunTest( globalInfo, scriptMustExist );
            }
        }

        /// <summary>
        /// Generates the .tgz file in the <see cref="StandardGlobalInfo.ReleasesFolder"/>
        /// by calling npm pack for all <see cref="PublishedProjects"/>.
        /// </summary>
        /// <param name="globalInfo">The global information object.</param>
        public void RunPack( StandardGlobalInfo globalInfo )
        {
            foreach( var p in PublishedProjects )
            {
                p.RunPack( globalInfo );
            }
        }

        /// <summary>
        /// Reads the "CodeCakeBuilder/NPMSolution.xml" file that must exist.
        /// </summary>
        /// <param name="version">The version of all published packages.</param>
        /// <returns>The solution object.</returns>
        public static NPMSolution ReadFromNPMSolutionFile( SVersion version )
        {
            var projects = XDocument.Load( "CodeCakeBuilder/NPMSolution.xml" ).Root
                            .Elements("Project")
                            .Select( p => (bool)p.Attribute( "IsPublished" )
                                            ? NPMPublishedProject.Load( (string)p.Attribute( "Path" ),
                                                                        (string)p.Attribute( "ExpectedName" ),
                                                                        version )
                                            : new NPMProject( (string)p.Attribute( "Path" ) ) );
            return new NPMSolution( projects );
        }
    }
}
