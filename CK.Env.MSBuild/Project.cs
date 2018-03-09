using CK.Core;
using CK.Text;
using CSemVer;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Xml.XPath;

namespace CK.Env.MSBuild
{
    /// <summary>
    /// Represents an actual project in a solution.
    /// </summary>
    public class Project : ProjectBase
    {
        readonly ProjectFileContext _ctx;
        ProjectFileContext.File _file;
        IReadOnlyList<DeclaredPackageDependency> _packageDependencies;

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
                Sdk = (string)_file.Document.Root.Attribute( "Sdk" );
                if( Sdk == null )
                {
                    m.Error( $"There must a Sdk element on root {Path}." );
                    _file = null;
                }
                else
                {
                    XElement f = _file.Document.Root
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

        public IReadOnlyList<DeclaredPackageDependency> GetPackageDependencies( IActivityMonitor m, bool force )
        {
            LoadProjectFile( m, force );
            if( _file == null ) return null;
            if( _packageDependencies == null || force )
            {
                _packageDependencies = null;
                var inital = _file.AllRoots
                                 .SelectMany( root => root.Elements( "ItemGroup" ).Elements( "PackageReference" ) )
                                 .Select( e => (InitialDeclaration: e,
                                                PackageId: (string)e.Attribute( "Include" ),
                                                RawVersion: (string)e.Attribute( "Version" ) ) );
                var deps = new List<DeclaredPackageDependency>();
                foreach( var p in inital )
                {
                    bool isPropVersion;
                    SVersion version = null;
                    if( String.IsNullOrWhiteSpace( p.PackageId )
                        || String.IsNullOrWhiteSpace( p.RawVersion )
                        || !(isPropVersion = p.RawVersion.StartsWith("$(" ))
                        || !(version = SVersion.TryParse( p.RawVersion )).IsValidSyntax )
                    {
                        if( version != null )
                        {
                            m.Error( $"Unable to parse Version attribute on element {p.InitialDeclaration}: {version.ParseErrorMessage}" );
                        }
                        else
                        {
                            m.Error( $"Invalid Include or Version attribute on element {p.InitialDeclaration}." );
                        }
                        return null;
                    }
                    XElement propertyDef = null;
                    if( isPropVersion )
                    {
                        if( !p.RawVersion.EndsWith( "Version)" ) )
                        {
                            m.Error( $"Invalid $(PropertyVersion) on element {p.InitialDeclaration}. Its name must end with Version." );
                            return null;
                        }
                        // Lookup for the property.
                        string propName = p.RawVersion.Substring( 2, p.RawVersion.Length - 3 );
                        var candidates = _file.AllRoots
                                             .SelectMany( root => root.Elements( "PropertyGroup" ) )
                                             .Where( e => e.Name.LocalName == propName )
                                             .ToList();
                        if( candidates.Count == 0 )
                        {
                            m.Error( $"Unable to find $({propName}) version definition for element {p.InitialDeclaration}." );
                            return null;
                        }
                        if( candidates.Count > 1 )
                        {
                            m.Error( $"Found more than one $({propName}) version definition for element {p.InitialDeclaration}." );
                            return null;
                        }
                        propertyDef = candidates[0];
                        version = SVersion.TryParse( propertyDef.Value );
                        if( !version.IsValidSyntax )
                        {
                            m.Error( $"Invalid $({propName}) version definition {p.InitialDeclaration} in {propertyDef}: {version.ParseErrorMessage}." );
                            return null;
                        }
                    }
                    deps.Add( new DeclaredPackageDependency( p.PackageId, version, p.InitialDeclaration, propertyDef ) );
                }
                _packageDependencies = deps;
            }
            return _packageDependencies;
        }

    }
}
