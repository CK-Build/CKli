using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env.MSBuildSln
{

    /// <summary>
    /// Central root class that manages <see cref="MSProjFile"/> loading and caching.
    /// </summary>
    public class MSProjContext
    {
        /// <summary>
        /// Traits are used to manage framework names.
        /// The <see cref="CKTraitContext.Separator"/> is the ';' to match the one used by csproj (parsing and
        /// string representation becomes straightforward).
        /// </summary>
        public static readonly CKTraitContext Traits = new CKTraitContext( "ProjectFileContext", ';' );

        readonly Dictionary<NormalizedPath, MSProjFile> _files;

        /// <summary>
        /// Initializes a new project file context.
        /// </summary>
        /// <param name="fileSystem">The file system provider.</param>
        public MSProjContext( FileSystem fileSystem )
        {
            FileSystem = fileSystem;
            _files = new Dictionary<NormalizedPath, MSProjFile>();
        }

        /// <summary>
        /// Gets the file system.
        /// </summary>
        public FileSystem FileSystem { get; }

        /// <summary>
        /// Finds from the cache or loads a Xml project file.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="path">The file path relative to the <see cref="FileSystem"/>.</param>
        /// <returns>The file or null if unable to load it.</returns>
        public MSProjFile FindOrLoadProjectFile( IActivityMonitor m, NormalizedPath path )
        {
            if( _files.TryGetValue( path, out MSProjFile f ) )
            {
                return f;
            }
            using( m.OpenTrace( $"Loading project file {path}." ) )
            {
                try
                {
                    var fP = FileSystem.GetFileInfo( path );
                    if( !fP.Exists )
                    {
                        m.Warn( $"Unable to find project file '{path}'. This project is ignored. This may be a case sensivity issue!" );
                        return null;
                    }
                    XDocument content = fP.ReadAsXDocument();
                    var imports = new List<MSProjFile.Import>();
                    f = new MSProjFile( path, content, imports );
                    _files[path] = f;
                    var folder = path.RemoveLastPart();
                    imports.AddRange( content.Root.Descendants( "Import" )
                                        .Select( i => (E: i, P: (string)i.Attribute( "Project" )) )
                                        .Where( i => i.P != null )
                                        .Select( i => new MSProjFile.Import( i.E, FindOrLoadProjectFile( m, folder.Combine( i.P ).ResolveDots() ) ) ) );

                    f.Initialize();
                    return f;
                }
                catch( Exception ex )
                {
                    m.Error( ex );
                    _files.Remove( path );
                    return null;
                }
            }
        }

        /// <summary>
        /// Unloads a project file.
        /// </summary>
        /// <param name="path">The path to the file.</param>
        /// <returns>True if the project has been actually unloaded.</returns>
        public bool UnloadFile( NormalizedPath path ) => _files.Remove( path );
    }
}
