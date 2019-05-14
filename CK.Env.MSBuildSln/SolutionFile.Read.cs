using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CK.Env.MSBuildSln
{
    public partial class SolutionFile
    {
        /// <summary>
        /// Reads or creates a new <see cref="SolutionFile"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="ctx">The project file context.</param>
        /// <param name="solutionPath">The path to the .sln file relative to the <see cref="MSProjContext.FileSystem"/>.</param>
        /// <param name="mustExist">False to allow the file to not exist.</param>
        /// <returns>
        /// The solution file or null on error (for example when not found and <paramref name="mustExist"/> is true).
        /// </returns>
        public static SolutionFile Read( IActivityMonitor m, MSProjContext ctx, NormalizedPath solutionPath, bool mustExist = true )
        {
            var file = ctx.FileSystem.GetFileInfo( solutionPath );
            if( !file.Exists )
            {
                if( mustExist )
                {
                    m.Error( $"File '{solutionPath}' not found. Unable to read the solution." );
                    return null;
                }
                m.Warn( $"File '{solutionPath}' not found. Creating an empty solution." );
            }
            var s = new SolutionFile( ctx, solutionPath );
            if( file.Exists )
            {
                using( var r = new Reader( m, file.CreateReadStream() ) )
                {
                    if( s.Read( r ) ) return s;
                }
            }
            return null;
        }

        class Reader : IDisposable
        {
            readonly StreamReader _reader;
            readonly bool _dispose;

            public Reader( IActivityMonitor m, Stream s )
                : this( m, new StreamReader( s ), true )
            {
            }

            public Reader( IActivityMonitor m, StreamReader r, bool dispose )
            {
                Monitor = m;
                _reader = r;
                _dispose = dispose;
            }

            public string Line { get; private set; }

            public int LineNumber { get; private set; }

            public IActivityMonitor Monitor { get; }

            public void Dispose()
            {
                if( _dispose ) _reader.Dispose();
            }

            public bool Forward( bool required = true )
            {
                while( (Line = _reader.ReadLine()) != null )
                {
                    Line = Line.Trim();
                    ++LineNumber;
                    if( Line.Length > 0 ) return true;
                }
                if( required ) Monitor.Error( "Unexpected end of file." );
                return false;
            }

            public bool MatchLineStartingWith( string start )
            {
                return Line != null
                       && Line.StartsWith( start, StringComparison.OrdinalIgnoreCase )
                       && Forward();
            }
        }

        const string PatternParseHeader = @"^(\s*|Microsoft Visual Studio Solution File.*|#.*|VisualStudioVersion.*|MinimumVisualStudioVersion.*)$";
        static readonly Regex _rParseHeader = new Regex( PatternParseHeader, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture );

        void ClearAll()
        {
            _headers.Clear();
            _projectIndex.Clear();
            _projectBaseList.Clear();
            _projectMSList.Clear();
            _sections.Clear();
        }

        bool Read( Reader r )
        {
            ClearAll();
            while( r.Forward() && _rParseHeader.IsMatch( r.Line ) )
            {
                AddHeader( r.Line );
            }
            while( r.Line.StartsWith( "Project(", StringComparison.OrdinalIgnoreCase ) )
            {
                var p = ReadProject( this, r );
                if( p == null ) return false;
                AddProject( p );
            }
            if( String.Equals( r.Line, "Global", StringComparison.OrdinalIgnoreCase ) )
            {
                while( r.Forward() && !r.Line.StartsWith( "EndGlobal", StringComparison.OrdinalIgnoreCase ) )
                {
                    if( !ReadGlobalSection( this, r ) ) return false;
                }
            }
            else
            {
                r.Monitor.Fatal( $"Invalid line read on line #{r.LineNumber}. Found: {r.Line}. Expected: A line beginning with 'Project(' or 'Global'." );
                return false;
            }
            bool hasError = false;
            foreach( var p in AllProjects )
            {
                hasError |= !p.Initialize( r.Monitor );
            }
            // Note that the project files may be dirty when they are cached
            // and not saved.
            SetDirtyStructure( false );
            return !hasError;
        }

        const string PatternParseProject = "^Project\\(\"(?<PROJECTTYPEGUID>.*)\"\\)\\s*=\\s*\"(?<PROJECTNAME>.*)\"\\s*,\\s*\"(?<RELATIVEPATH>.*)\"\\s*,\\s*\"(?<PROJECTGUID>.*)\"$";
        static readonly Regex _rParseProject = new Regex( PatternParseProject, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture );

        static ProjectBase ReadProject( SolutionFile s, Reader r )
        {
            var match = _rParseProject.Match( r.Line );
            if( !match.Success )
            {
                r.Monitor.Fatal( $"Invalid format for a project on line #{r.LineNumber}.\nFound: {r.Line}. Expected: A line starting with 'Global' or respecting the pattern '{PatternParseProject}'." );
                return null;
            }
            var projectTypeGuid = match.Groups["PROJECTTYPEGUID"].Value.Trim();
            var projectName = match.Groups["PROJECTNAME"].Value.Trim();
            var relativePath = match.Groups["RELATIVEPATH"].Value.Trim();
            var projectGuid = match.Groups["PROJECTGUID"].Value.Trim();

            ProjectBase result;
            var projectType = ProjectType.Parse( projectTypeGuid );
            if( projectType == KnownProjectType.SolutionFolder )
            {
                result = new SolutionFolder( s, projectGuid, relativePath );
            }
            else if( projectType.IsVSProject() )
            {
                result = new MSProject( s, projectType, projectGuid, projectName, relativePath );
            }
            else
            {
                r.Monitor.Warn( $"Project Type {projectTypeGuid} is unhandled for {relativePath} in {s.FilePath}." );
                result = new Project( s, projectGuid, projectTypeGuid, projectName, relativePath );
            }
            while( r.Forward() && !r.MatchLineStartingWith( "EndProject" ) )
            {
                result.AddSection( ReadProjectSection( result, r ) );
            }
            return result;
        }

        const string PatternParseProjectSection = @"^ProjectSection\((?<NAME>.*)\) = (?<STEP>.*)$";
        static readonly Regex _rParseProjectSection = new Regex( PatternParseProjectSection, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture );

        static Section ReadProjectSection( ProjectBase project, Reader r )
        {
            var match = _rParseProjectSection.Match( r.Line );
            if( !match.Success )
            {
                r.Monitor.Fatal( $"Invalid format for a project section on line #{r.LineNumber}. Found: {r.Line}. Expected: A line starting with 'EndProject' or respecting the pattern '{PatternParseProjectSection}'." );
                return null;
            }
            var name = match.Groups["NAME"].Value.Trim();
            var step = match.Groups["STEP"].Value.Trim();

            var props = ReadPropertyLines( r, "EndProjectSection" );
            if( props == null ) return null;

            return new Section( project, name, step, props );
        }

        private const string PatternParseGlobalSection = @"^GlobalSection\((?<NAME>.*)\) = (?<STEP>.*)$";
        private static readonly Regex _rParseGlobalSection = new Regex( PatternParseGlobalSection, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture );

        static bool ReadGlobalSection( SolutionFile s, Reader r )
        {
            var match = _rParseGlobalSection.Match( r.Line );
            if( !match.Success )
            {
                r.Monitor.Fatal( $"Invalid format for a global section on line #{r.LineNumber}. Found: {r.Line}. Expected: A line starting with 'EndGlobal' or respecting the pattern '{PatternParseGlobalSection}'." );
                return false;
            }
            var name = match.Groups["NAME"].Value.Trim();
            var step = match.Groups["STEP"].Value.Trim();

            var propertyLines = ReadPropertyLines( r, "EndGlobalSection" );
            if( propertyLines == null ) return false;

            Section section = null;
            switch( name )
            {
                case "NestedProjects":
                    {
                        section = HandleNestedProjects( s, r, step, propertyLines.Values );
                        break;
                    }

                case "ProjectConfigurationPlatforms":
                    {
                        section = HandleProjectConfigurationPlatforms( s, r, step, propertyLines.Values );
                        break;
                    }
                default:
                    if( name.EndsWith( "Control", StringComparison.OrdinalIgnoreCase ) )
                    {
                        section = HandleVersionControlLines( s, r, name, step, propertyLines );
                    }
                    else if( s.FindGlobalSection( name ) != null )
                    {
                        r.Monitor.Warn( $"Duplicate global section '{name}' found in solution." );
                        return true;
                    }
                    else section = new Section( s, name, step, propertyLines );
                    break;
            }

            if( section == null ) return false;
            s.AddGlobalSection( section );
            return true;
        }

        static Section HandleNestedProjects(
            SolutionFile s,
            Reader r,
            string step,
            IEnumerable<PropertyLine> propertyLines )
        {
            foreach( var propertyLine in propertyLines )
            {
                var left = s.FindProjectByGuid( r.Monitor, propertyLine.Name, propertyLine.LineNumber );
                if( left == null ) return null;
                left.ParentFolderGuid = propertyLine.Value;
            }
            return new Section( s, "NestedProjects", step );
        }

        const string PatternParseProjectConfigurationPlatformsName = @"^(?<GUID>\{[-0-9a-zA-Z]+\})\.(?<DESCRIPTION>.*)$";
        static readonly Regex _rParseProjectConfigurationPlatformsName = new Regex( PatternParseProjectConfigurationPlatformsName, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture );

        static Section HandleProjectConfigurationPlatforms( SolutionFile s, Reader r, string step, IEnumerable<PropertyLine> propertyLines )
        {
            foreach( var propertyLine in propertyLines )
            {
                var match = _rParseProjectConfigurationPlatformsName.Match( propertyLine.Name );
                if( !match.Success )
                {
                    r.Monitor.Fatal( $"Invalid format for a project configuration name on line #{r.LineNumber}. Found: {r.Line}. Expected: A line respecting the pattern '{PatternParseProjectConfigurationPlatformsName}'." );
                    return null;
                }

                var projectGuid = match.Groups["GUID"].Value;
                var description = match.Groups["DESCRIPTION"].Value;
                var left = s.FindProjectByGuid( r.Monitor, projectGuid, propertyLine.LineNumber );
                if( left is Project p )
                {
                    p.AddProjectConfigurationPlatform( new PropertyLine( description, propertyLine.Value ) );
                }
                else
                {
                    r.Monitor.Warn( $"Project configuration targets a Solution folder. Line #{r.LineNumber}: {r.Line}." );
                }
            }
            return new Section( s, "ProjectConfigurationPlatforms", step );
        }

        private static readonly Regex _rParseVersionControlName = new Regex( @"^(?<NAME_WITHOUT_INDEX>[a-zA-Z]*)(?<INDEX>[0-9]+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture );
        private static readonly Regex _rConvertEscapedValues = new Regex( @"\\u(?<HEXACODE>[0-9a-fA-F]{4})", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture );

        static Section HandleVersionControlLines( SolutionFile s, Reader r, string name, string step, Dictionary<string, PropertyLine> propertyLines )
        {
            var propertyLinesByIndex = new Dictionary<int, List<PropertyLine>>();
            foreach( var prop in propertyLines.Values )
            {
                var match = _rParseVersionControlName.Match( prop.Name );
                if( match.Success )
                {
                    var nameWithoutIndex = match.Groups["NAME_WITHOUT_INDEX"].Value.Trim();
                    var index = int.Parse( match.Groups["INDEX"].Value.Trim() );
                    if( !propertyLinesByIndex.TryGetValue( index, out var list ) )
                    {
                        propertyLinesByIndex[index] = list = new List<PropertyLine>();
                    }
                    propertyLinesByIndex[index].Add( new PropertyLine( nameWithoutIndex, prop.Value ) );
                    propertyLines.Remove( prop.Name );
                }
                else
                {
                    // Skips SccNumberOfProjects since this is a computed value.
                    if( prop.Name == "SccNumberOfProjects" ) propertyLines.Remove( prop.Name );
                }
            }

            // Handle the special case for the solution itself.
            if( !propertyLines.TryGetValue( "SccLocalPath0", out var localPath )
                || localPath.Value != "." )
            {
                propertyLines["SccLocalPath0"] = new PropertyLine( "SccLocalPath0", "." );
            }

            foreach( var item in propertyLinesByIndex )
            {
                var index = item.Key;
                var propertiesForIndex = item.Value;

                var uniqueNameProperty = propertiesForIndex.Find( property => property.Name == "SccProjectUniqueName" );

                // If there is no ProjectUniqueName, we assume that it's the entry related to the solution by itself. We
                // can ignore it because we added the special case above.
                if( uniqueNameProperty != null )
                {
                    var uniqueName = _rConvertEscapedValues.Replace( uniqueNameProperty.Value, match =>
                                 {
                                     var hexaValue = int.Parse( match.Groups["HEXACODE"].Value, NumberStyles.AllowHexSpecifier );
                                     return char.ConvertFromUtf32( hexaValue );
                                 } );
                    uniqueName = uniqueName.Replace( @"\\", @"\" );

                    Project relatedProject = null;
                    foreach( var project in s.Projects )
                    {
                        if( string.Compare( project.SolutionRelativePath, uniqueName, StringComparison.OrdinalIgnoreCase ) == 0 )
                        {
                            relatedProject = project;
                        }
                    }
                    if( relatedProject == null )
                    {
                        r.Monitor.Fatal( $"Invalid value for the property 'SccProjectUniqueName{index}' of the global section '{name}'. Found: {uniqueName}. Expected: A value equal to the field 'RelativePath' of one of the projects in the solution (that is not a Solution folder)." );
                        return null;
                    }
                    foreach( var p in propertiesForIndex )
                    {
                        relatedProject.AddVersionControl( p );
                    }
                }
            }

            return new Section( s, name, step, propertyLines );
        }

        const string PatternParsePropertyLine = @"^(?<PROPERTYNAME>[^=]*)\s*=\s*(?<PROPERTYVALUE>[^=]*)$";
        static readonly Regex _rParsePropertyLine = new Regex( PatternParsePropertyLine, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture );

        static Dictionary<string,PropertyLine> ReadPropertyLines( Reader r, string endOfSectionToken )
        {
            PropertyLine ReadPropertyLine()
            {
                var match = _rParsePropertyLine.Match( r.Line );
                if( !match.Success )
                {
                    r.Monitor.Fatal( $"Invalid format for a property on line #{r.LineNumber}. Found: {r.Line}. Expected: A line starting with '{endOfSectionToken}' or respecting the pattern '{PatternParsePropertyLine}'." );
                    return null;
                }
                return new PropertyLine( match.Groups["PROPERTYNAME"].Value.Trim(), match.Groups["PROPERTYVALUE"].Value.Trim(), r.LineNumber );
            }

            var lines = new Dictionary<string, PropertyLine>( StringComparer.OrdinalIgnoreCase );
            var startLineNumber = r.LineNumber;
            while( r.Forward() && !r.Line.StartsWith( endOfSectionToken, StringComparison.OrdinalIgnoreCase ) )
            {
                var l = ReadPropertyLine();
                if( l == null ) return null;
                if( lines.ContainsKey( l.Name ) )
                {
                    r.Monitor.Error( $"Duplicate entry line # {r.LineNumber}: {l.Name} is already registered." );
                    return null;
                }
                lines.Add( l.Name, l );
            }
            return lines;
        }

    }
}
