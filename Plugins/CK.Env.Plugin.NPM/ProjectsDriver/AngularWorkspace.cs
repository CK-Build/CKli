using CK.Core;
using CK.Text;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env.Plugin
{
    public class AngularWorkspace
    {
        AngularWorkspace( NPMProjectsDriver driver, IAngularWorkspaceSpec spec, IReadOnlyCollection<NPMProject> projects )
        {
            Driver = driver;
            Specification = spec;
            Projects = projects;
            FullPath = Driver.BranchPath.Combine( spec.Path );
        }

        static internal AngularWorkspace LoadAngularSolution( NPMProjectsDriver driver, IActivityMonitor m, IAngularWorkspaceSpec spec )
        {
            NormalizedPath packageJsonPath = spec.Path.AppendPart( "package.json" );
            NormalizedPath angularJsonPath = spec.Path.AppendPart( "angular.json" );
            var fs = driver.GitFolder.FileSystem;
            NormalizedPath path = driver.BranchPath;
            JObject packageJson = fs.GetFileInfo( path.Combine( packageJsonPath ) ).ReadAsJObject();
            JObject angularJson = fs.GetFileInfo( path.Combine( angularJsonPath ) ).ReadAsJObject();
            if( !(packageJson["private"]?.ToObject<bool?>() ?? false) )
            {
                throw new InvalidDataException( $"A workspace project should be private. File '{packageJsonPath}' must be fixed with a \"private\": true property." );
            }
            if( angularJson["projects"] is not JObject jProjects )
            {
                throw new InvalidDataException( $"Missing \"projects\" in '{packageJsonPath}'." );
            }
            List<NPMProject> projects = new();
            foreach( var propProject in jProjects.Properties() )
            {
                var name = propProject.Name;
                var projPathRelativeToWorkspace = new NormalizedPath( propProject.Value["root"]?.ToString() );
                if( projPathRelativeToWorkspace.IsEmptyPath )
                {
                    m.Warn( $"Project '{name}' is missing \"root\" property. It is ignored." );
                }
                else
                {
                    var projPathRelativeToGitRepo = spec.Path.Combine( projPathRelativeToWorkspace );

                    var packageFile = fs.GetFileInfo( driver.BranchPath.Combine( projPathRelativeToGitRepo ).AppendPart( "package.json" ) );
                    if( !packageFile.Exists )
                    {
                        m.Warn( $"File '{packageFile}' not found. Project '{name}' is ignored." );
                    }
                    else
                    {
                        JObject json = packageFile.ReadAsJObject();
                        var projectName = json["name"]?.ToString();
                        if( string.IsNullOrWhiteSpace( projectName ) )
                        {
                            m.Warn( $"Missing or empty \"name\" in '{packageFile}'. Project '{name}' is ignored." );
                        }
                        else
                        {
                            bool isPrivate = json["private"]?.ToObject<bool>() ?? false;
                            projects.Add( new NPMProject( driver, m, new NPMProjectSpec( projPathRelativeToGitRepo, projectName, isPrivate ) ) );
                        }
                    }
                }
            }
            return new AngularWorkspace( driver, spec, projects );
        }

        public IReadOnlyCollection<NPMProject> Projects { get; }

        public NormalizedPath OutputDir { get; }

        /// <summary>
        /// Gets the driver plugin.
        /// </summary>
        public NPMProjectsDriver Driver { get; }

        /// <summary>
        /// Gets the solution-project specification.
        /// </summary>
        public IAngularWorkspaceSpec Specification { get; }

        /// <summary>
        /// Gets the project-solution folder path relative to the <see cref="FileSystem"/>.
        /// </summary>
        public NormalizedPath FullPath { get; }

        public XElement ToXml()
        {
            return new XElement( "AngularWorkspace",
                new XAttribute( "Path", Specification.Path ),
                new XAttribute( "OutputFolder", Specification.OutputFolder ) );
        }
    }
}
