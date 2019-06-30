using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CK.Env.MSBuildSln
{
    public partial class SolutionFile
    {

        /// <summary>
        /// Writes the solution file to the writer.
        /// </summary>
        /// <param name="w">The writer to write to.</param>
        public void Write( TextWriter w )
        {
            WriteHeader( w );
            WriteProjects( w );
            WriteGlobal( w );
        }

        void WriteHeader( TextWriter w )
        {
            // If the header doesn't start with an empty line, add one
            // (The first line of sln files saved as UTF-8 with BOM must be blank, otherwise
            // Visual Studio Version Selector will not detect VS version correctly.)
            if( Headers.Count == 0 || Headers[0].Trim().Length > 0 )
            {
                w.WriteLine();
            }

            foreach( var line in Headers )
            {
                w.WriteLine( line );
            }
        }

        void WriteProjects( TextWriter w )
        {
            foreach( var project in AllProjects )
            {
                w.WriteLine( "Project(\"{0}\") = \"{1}\", \"{2}\", \"{3}\"",
                                project.ProjectTypeGuid,
                                project.ProjectName,
                                project.SolutionRelativePath,
                                project.ProjectGuid );
                if( project is SolutionFolder f )
                {
                    WriteSection( w, "ProjectSection", "SolutionItems", "preProject",
                                        f.Items.Select( item => new PropertyLine( item, item ) ) );
                }
                foreach( var projectSection in project.ProjectSections )
                {
                    WriteSection( w, projectSection, projectSection.PropertyLines );
                }
                w.WriteLine( "EndProject" );
            }
        }

        void WriteGlobal(TextWriter w )
        {
            w.WriteLine("Global");
            foreach( var s in GlobalSections )
            {
                WriteGlobalSection( w, s );
            }
            w.WriteLine("EndGlobal");
        }

        void WriteGlobalSection( TextWriter w, Section s )
        {
            var propLines = new List<PropertyLine>( s.PropertyLines );
            switch( s.Name )
            {
                case "NestedProjects":
                    foreach( var p in AllProjects )
                    {
                        if( p.ParentFolderGuid != null )
                        {
                            propLines.Add( new PropertyLine( p.ProjectGuid, p.ParentFolderGuid ) );
                        }
                    }
                    break;

                case "ProjectConfigurationPlatforms":
                    foreach( var p in Projects )
                    {
                        foreach( var propertyLine in p.ProjectConfigurationPlatformsLines )
                        {
                            propLines.Add( new PropertyLine( $"{p.ProjectGuid}.{propertyLine.Name}", propertyLine.Value ) );
                        }
                    }
                    break;

                default:
                    if( s.Name.EndsWith( "Control", StringComparison.OrdinalIgnoreCase ) )
                    {
                        var index = 1;
                        foreach( var p in Projects )
                        {
                            if( p.VersionControlLines.Count > 0 )
                            {
                                foreach( var prop in p.VersionControlLines )
                                {
                                    propLines.Add( new PropertyLine( $"{prop.Name}{index}", prop.Value ) );
                                }
                                index++;
                            }
                        }
                        propLines.Insert( 0, new PropertyLine( "SccNumberOfProjects", index.ToString() ) );
                    }
                    break;
            }
            WriteSection( w, s, propLines );
        }

        static void WriteSection( TextWriter w, Section section, IEnumerable<PropertyLine> propertyLines )
        {
            WriteSection( w, section.SectionType, section.Name, section.Step, propertyLines );
        }

        static void WriteSection( TextWriter w, string sectionType, string name, string step, IEnumerable<PropertyLine> propertyLines )
        {
            w.WriteLine( "\t{0}({1}) = {2}", sectionType, name, step );
            foreach( var propertyLine in propertyLines )
            {
                w.WriteLine( "\t\t{0} = {1}", propertyLine.Name, propertyLine.Value );
            }
            w.WriteLine( "\tEnd{0}", sectionType );
        }
    }
}
