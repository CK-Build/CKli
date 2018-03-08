using CK.Core;
using CK.Text;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env.MSBuild
{
    /// <summary>
    /// Represents an actual project in a solution.
    /// </summary>
    public class Project : ProjectBase
    {
        readonly ProjectFileContext _ctx;
        ProjectFileContext.File _file;

        /// <summary>
        /// Initializes a new <see cref="Project"/> instance.
        /// </summary>
        /// <param name="id">The folder project identity.</param>
        /// <param name="name">The folder name.</param>
        /// <param name="path">The folder path.</param>
        public Project( string id, string name, NormalizedPath projectFilePath, string typeIdentifier, ProjectFileContext ctx )
            : base( id, name, projectFilePath, typeIdentifier )
        {
            _ctx = ctx;
        }

        public bool IsCSProj => Path.LastPart.EndsWith( ".csproj" );

        /// <summary>
        /// Gets the project file. This is loaded when the <see cref="SolutionFile"/>
        /// is created but may be reloaded.
        /// This is null if an error occurred while loading.
        /// </summary>
        public ProjectFileContext.File ProjectFile => _file;

        public ProjectFileContext.File LoadProjectFile( IActivityMonitor m, bool force = false )
        {
            if( !force && _file != null ) return _file;
            _file = _ctx.FindOrLoad( m, Path, force );
            if( _file != null )
            {
                Sdk = (string)_file.Content.Root.Attribute( "Sdk" );
                if( Sdk == null )
                {
                    m.Error( $"There must be one and only one TargetFramework or TargetFrameworks element in {Path}." );
                    _file = null;
                }
                else
                {
                    XElement f = _file.Content.Root
                                    .Elements( "PropertyGroup" )
                                    .Elements()
                                    .Where( x => x.Name.LocalName == "TargetFramework" || x.Name.LocalName == "TargetFrameworks" )
                                    .SingleOrDefault();
                    if( f == null )
                    {
                        m.Error( $"There must be one and only one TargetFramework or TargetFrameworks element in {Path}." );
                        _file = null;
                    }
                    else
                    {
                        TargetFrameworks = f.Value.Split( new[] { ';' }, StringSplitOptions.RemoveEmptyEntries );
                        m.Debug( $"TargetFrameworks = {TargetFrameworks.Concatenate()}" );
                    }
                }
            }
            if( _file == null )
            {
                Sdk = null;
                TargetFrameworks = Array.Empty<string>();
            }
            return _file;
        }

        public string Sdk { get; private set; }

        public IReadOnlyList<string> TargetFrameworks { get; private set; }

    }
}
