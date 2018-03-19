using CK.Core;
using CK.Setup;
using CK.Text;
using CSemVer;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        /// <summary>
        /// Captures <see cref="DeclaredPackageDependency"/> and <see cref="ProjectToProjectDependency"/>.
        /// </summary>
        public struct Dependencies
        {
            public readonly IReadOnlyList<DeclaredPackageDependency> Packages;
            public readonly IReadOnlyList<ProjectToProjectDependency> Projects;
            public bool IsInitialized => Packages != null;

            internal Dependencies( IReadOnlyList<DeclaredPackageDependency> packages, IReadOnlyList<ProjectToProjectDependency> projects )
            {
                Packages = packages;
                Projects = projects;
            }
        }

        readonly ProjectFileContext _ctx;
        ProjectFileContext.File _file;
        Dependencies _dependencies;

        internal Project( string id, string name, NormalizedPath projectFilePath, string typeIdentifier, ProjectFileContext ctx )
            : base( id, name, projectFilePath, typeIdentifier )
        {
            _ctx = ctx;
        }

        public bool IsCSProj => Path.LastPart.EndsWith( ".csproj" );

        /// <summary>
        /// Gets the project file. This is loaded when the <see cref="SolutionFile"/>
        /// is created but may be reloaded if needed.
        /// This is null if an error occurred while loading.
        /// </summary>
        public ProjectFileContext.File ProjectFile => _file;

        /// <summary>
        /// Enables the <see cref="ProjectFile"/> to be reloaded.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="force">True to force the reload.</param>
        /// <returns>The project file and all its imports.</returns>
        public ProjectFileContext.File LoadProjectFile( IActivityMonitor m, bool force = false )
        {
            if( !force && _file != null ) return _file;
            IsTestProject = false;
            _dependencies = new Dependencies();
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
                        TargetFrameworks = ProjectFileContext.Traits.EmptyTrait;
                        foreach( var t in f.Value.Split( new[] { ';' }, StringSplitOptions.RemoveEmptyEntries ) )
                        {
                            TargetFrameworks = TargetFrameworks.Union( ProjectFileContext.Traits.FindOrCreate( t ) );
                        }
                        m.Debug( $"TargetFrameworks = {TargetFrameworks}" );
                        bool? isTestProject = (bool?)_file.Document.Root.Elements( "PropertyGroup" )
                                                          .Elements( "IsTestProject" )
                                                          .FirstOrDefault();
                        if( !isTestProject.HasValue )
                        {
                            var testMarker = _file.Document.Root.Elements( "ItemGroup" )
                                                  .Elements( "Service" )
                                                  .FirstOrDefault( e => (string)e.Attribute( "Include" ) == "{82a7f48d-3b50-4b1e-b82e-3ada8210c358}" );
                            isTestProject = testMarker != null;
                        }
                        IsTestProject = isTestProject.Value;
                    }
                }
            }
            if( _file == null )
            {
                Sdk = null;
                TargetFrameworks = ProjectFileContext.Traits.EmptyTrait;
            }
            return _file;
        }

        /// <summary>
        /// Gets the Sdk attribute of the primary project file.
        /// Null if the project can not be read.
        /// </summary>
        public string Sdk { get; private set; }

        /// <summary>
        /// Gets the target frameforks from the primary project file.
        /// Null if the project can not be read.
        /// </summary>
        public CKTrait TargetFrameworks { get; private set; }

        /// <summary>
        /// Gets whether this is a test project. Since VS 15.6.1 update the
        /// csproj contains a Service Include="{82a7f48d-3b50-4b1e-b82e-3ada8210c358}" item.
        /// </summary>
        public bool IsTestProject { get; private set; }

        /// <summary>
        /// Gets the dependencies.
        /// They must have been <see cref="InitializeDeps"/> first.
        /// </summary>
        public Dependencies Deps => _dependencies;

        public Dependencies InitializeDeps( IActivityMonitor m, bool forceReload )
        {
            using( m.OpenTrace( $"Reading dependencies of {ToString()}." ) )
            {
                LoadProjectFile( m, forceReload );
                if( _file == null )
                {
                    Debug.Assert( !_dependencies.IsInitialized );
                    return _dependencies;
                }
                if( !_dependencies.IsInitialized || forceReload )
                {
                    DoInitializeDependencies( m );
                }
                return _dependencies;
            }
        }

        public override string ToString() => $"Project '{Name}' in '{Solution}'.";

        void DoInitializeDependencies( IActivityMonitor m )
        {
            _dependencies = new Dependencies();
            var packageRefs = _file.AllRoots
                             .SelectMany( root => root.Elements( "ItemGroup" )
                                                        .Elements()
                                                        .Where( e => e.Name.LocalName == "PackageReference"
                                                                     || e.Name.LocalName == "ProjectReference" ) )
                             .Select( e => (Origin: e,
                                            PackageId: (string)e.Attribute( "Include" ),
                                            RawVersion: (string)e.Attribute( "Version" )) );

            var conditionEvaluator = new PartialEvaluator();
            var deps = new List<DeclaredPackageDependency>();
            var projs = new List<ProjectToProjectDependency>();
            foreach( var p in packageRefs )
            {
                if( p.Origin.Name.LocalName == "PackageReference" )
                {
                    bool isPropVersion;
                    SVersion version = null;
                    if( String.IsNullOrWhiteSpace( p.PackageId )
                        || String.IsNullOrWhiteSpace( p.RawVersion )
                        || (
                             !(isPropVersion = p.RawVersion.StartsWith( "$(" ))
                             && !(version = SVersionRange.TryParseSimpleRange( p.RawVersion )).IsValidSyntax
                           ) )
                    {
                        if( version != null )
                        {
                            m.Error( $"Unable to parse Version attribute on element {p.Origin}: {version.ParseErrorMessage}" );
                        }
                        else
                        {
                            m.Error( $"Invalid Include or Version attribute on element {p.Origin}." );
                        }
                        return;
                    }
                    XElement propertyDef = null;
                    if( isPropVersion )
                    {
                        propertyDef = FollowRefPropertyVersion( m, p, ref version );
                        if( propertyDef == null ) return;
                    }
                    CKTrait frameworks = ComputeFrameworks( m, p.Origin, conditionEvaluator );
                    if( frameworks == null ) return;
                    deps.Add( new DeclaredPackageDependency( this, p.PackageId, version, p.Origin, propertyDef, frameworks ) );
                }
                else
                {
                    string projectName = new NormalizedPath( p.PackageId ).LastPart;
                    if( !projectName.EndsWith( ".csproj" ) )
                    {
                        m.Error( $"ProjectReference must Include a .csproj project: {p.Origin}." );
                        return;
                    }
                    projectName = projectName.Substring( 0, projectName.Length - 7 );
                    var target = Solution.AllProjects.FirstOrDefault( pRef => pRef.Name == projectName );
                    if( target == null )
                    {
                        m.Error( $"ProjectReference '{p.PackageId}' not found in the solution. Project name '{projectName}' must exist in the solution." );
                        return;
                    }
                    CKTrait frameworks = ComputeFrameworks( m, p.Origin, conditionEvaluator );
                    if( frameworks == null ) return;
                    projs.Add( new ProjectToProjectDependency( this, target, frameworks ) );
                }
            }
            _dependencies = new Dependencies( deps, projs );
        }

        CKTrait ComputeFrameworks( IActivityMonitor m, XElement e, PartialEvaluator evaluator )
        {
            CKTrait frameworks = TargetFrameworks;
            foreach( var framework in TargetFrameworks.AtomicTraits )
            {
                foreach( var condition in e.AncestorsAndSelf()
                                       .Select( x => (E: x, C: (string)x.Attribute( "Condition" )) )
                                       .Where( x => x.C != null ) )
                {
                    bool? include = evaluator.EvalFinalResult( m, condition.C, f => f == "$(TargetFramework)" ? framework.ToString() : null );
                    if( include == null )
                    {
                        m.Error( $"Unable to evaluate condition of {condition.E}." );
                        return null;
                    }
                    if( include == false )
                    {
                        frameworks = frameworks.Except( framework );
                    }
                }
            }
            return frameworks;
        }

        XElement FollowRefPropertyVersion( IActivityMonitor m, (XElement Origin, string PackageId, string RawVersion) p, ref SVersion version )
        {
            if( !p.RawVersion.EndsWith( "Version)" ) )
            {
                m.Error( $"Invalid $(PropertyVersion) on element {p.Origin}. Its name must end with Version." );
                return null;
            }
            // Lookup for the property.
            string propName = p.RawVersion.Substring( 2, p.RawVersion.Length - 3 );
            var candidates = _file.AllRoots
                                 .SelectMany( root => root.Elements( "PropertyGroup" ) )
                                 .Elements()
                                 .Where( e => e.Name.LocalName == propName ).ToList();
            if( candidates.Count == 0 )
            {
                m.Error( $"Unable to find $({propName}) version definition for element {p.Origin}." );
                return null;
            }
            if( candidates.Count > 1 )
            {
                m.Error( $"Found more than one $({propName}) version definition for element {p.Origin}." );
                return null;
            }
            XElement propertyDef = candidates[0];
            version = SVersionRange.TryParseSimpleRange( propertyDef.Value );
            if( !version.IsValidSyntax )
            {
                m.Error( $"Invalid $({propName}) version definition {p.Origin} in {propertyDef}: {version.ParseErrorMessage}." );
                return null;
            }
            return propertyDef;
        }
    }
}
