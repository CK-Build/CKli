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
    public class NPMSolution
    {
        NPMSolution( IEnumerable<NPMProject> projects )
        {
            Projects = projects.ToArray();
            PublishedProjects = Projects.OfType<NPMPublishedProject>().ToArray();
        }

        public IReadOnlyList<NPMProject> Projects { get; }

        public IReadOnlyList<NPMPublishedProject> PublishedProjects { get; }


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
