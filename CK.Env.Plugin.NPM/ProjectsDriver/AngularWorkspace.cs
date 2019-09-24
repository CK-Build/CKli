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
            if( !(packageJson["private"]?.ToObject<bool?>() ?? false) ) throw new InvalidDataException( "A workspace project should be private." );
            string solutionName = packageJson["name"].ToString();
            List<string> unscopedNames = angularJson["projects"].ToObject<JObject>().Properties().Select( p => p.Name ).ToList();

            List<NPMProject> projects = unscopedNames.Select(
                unscopedName =>
                {
                    var projPathRelativeToWorkspace = new NormalizedPath( angularJson["projects"][unscopedName]["root"].ToString() );
                    var projPathRelativeToGitRepo = spec.Path.Combine( projPathRelativeToWorkspace );
                    var projectPathVirtualPath = driver.BranchPath.Combine( projPathRelativeToGitRepo );
                    JObject json = fs.GetFileInfo( projectPathVirtualPath.AppendPart( "package.json" ) ).ReadAsJObject();
                    return new NPMProject(
                        driver,
                        m,
                        new NPMProjectSpec(
                            projPathRelativeToGitRepo,
                            json["name"].ToString(),
                            json["private"]?.ToObject<bool>() ?? false
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
                new XAttribute( "OutputFolder", Specification.OutputFolder ) );
        }
    }
}
