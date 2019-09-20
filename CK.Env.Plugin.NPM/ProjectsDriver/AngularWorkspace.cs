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
            JObject packageJson = fs.GetFileInfo( packageJsonPath ).ReadAsJObject();
            JObject angularJson = fs.GetFileInfo( angularJsonPath ).ReadAsJObject();
            if( !angularJson["private"].ToObject<bool>() ) throw new InvalidDataException( "A workspace project should be private." );
            string solutionName = packageJson["name"].ToString();
            List<string> names = angularJson["projects"].ToObject<JObject>().Properties().Select( p => p.Name ).ToList();

            List<NPMProject> projects = names.Select(
                p =>
                {
                    var projectPath = new NormalizedPath( angularJson["projects"][p]["root"].ToString() );
                    return new NPMProject(
                        driver,
                        m,
                        new NPMProjectSpec(
                            projectPath,
                            p,
                            fs.GetFileInfo( spec.Path.AppendPart( projectPath ).AppendPart( "package.json" ) ).ReadAsJObject()["private"].ToObject<bool>()
                        )
                    );
                } ).ToList();
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
                new XAttribute( "OutputDir", Specification.OutputPath) );
        }
    }
}
