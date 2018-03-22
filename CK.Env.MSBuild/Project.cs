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
            public readonly IReadOnlyList<XElement> UselessDependencies;
            public bool IsInitialized => Packages != null;

            internal Dependencies(
                IReadOnlyList<DeclaredPackageDependency> packages,
                IReadOnlyList<ProjectToProjectDependency> projects,
                IReadOnlyList<XElement> uselessDeps )
            {
                Packages = packages;
                Projects = projects;
                UselessDependencies = uselessDeps;
            }
        }

        readonly MSBuildContext _ctx;
        MSBuildContext.File _file;
        Dependencies _dependencies;

        internal Project( MSBuildContext ctx, string id, string name, NormalizedPath projectFilePath, string typeIdentifier )
            : base( id, name, projectFilePath, typeIdentifier )
        {
            _ctx = ctx;
        }

        /// <summary>
        /// Gets the project file. This is loaded when the <see cref="Solution"/>
        /// is created.
        /// This is null if an error occurred while loading.
        /// </summary>
        public MSBuildContext.File ProjectFile => _file;

        internal MSBuildContext.File ReloadProjectFile( IActivityMonitor m )
        {
            IsTestProject = false;
            _dependencies = new Dependencies();
            _file = _ctx.FindOrLoad( m, Path );
            if( _file != null )
            {
                Sdk = (string)_file.Document.Root.Attribute( "Sdk" );
                if( Sdk == null )
                {
                    m.Error( $"There must be a Sdk element on root {Path}." );
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
                        TargetFrameworks = MSBuildContext.ParseSemiColonFrameworks( f.Value );
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
                        DoInitializeDependencies( m );
                    }
                }
            }
            if( _file == null )
            {
                Sdk = null;
                TargetFrameworks = MSBuildContext.Traits.EmptyTrait;
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
        /// However sometimes it is not updated or it appears in a non test project (in a
        /// helper assembly for example): to fix this, projects can define a IsTestProject
        /// property (sets to True or False) that is checked first. Only if IsTestProject
        /// is not defined, Service Include is used.
        /// </summary>
        public bool IsTestProject { get; private set; }

        /// <summary>
        /// Gets the dependencies.
        /// </summary>
        public Dependencies Deps => _dependencies;

        /// <summary>
        /// Sets a package reference and returns the number of changes.
        /// </summary>
        /// <param name="m">The monitor.</param>
        /// <param name="frameworks">Frameworks that applies to the reference.</param>
        /// <param name="packageId">The package identifier.</param>
        /// <param name="version">The new version to set.</param>
        public int SetPackageReferenceVersion( IActivityMonitor m, CKTrait frameworks, string packageId, SVersion version )
        {
            if( !_dependencies.IsInitialized ) throw new InvalidOperationException( "Invalid Project." );
            int changeCount = 0;
            foreach( var r in _dependencies.Packages.Where( p => p.PackageId == packageId
                                                                 && p.Version != version
                                                                 && p.Frameworks.Intersect( frameworks ).IsEmpty == false ) )
            {
                var e = r.PropertyVersionElement;
                if( e != null )
                {
                    e.Value = version.ToString();
                }
                else
                {
                    e = r.OriginElement;
                    e.Attribute( "Version" ).SetValue( version.ToString() );
                }
                ++changeCount;
            }
            m.Trace( $"{changeCount} version update in {ToString()} for package reference {packageId}." );
            if( changeCount > 0 ) DoInitializeDependencies( m );
            return changeCount;
        }

        public override string ToString() => $"Project '{Name}' in '{Solution}'.";

        void DoInitializeDependencies( IActivityMonitor m )
        {
            _dependencies = new Dependencies();
            var packageRefs = _file.AllFiles.Select( f => f.Document.Root )
                             .SelectMany( root => root.Elements( "ItemGroup" )
                                                        .Elements()
                                                        .Where( e => e.Name.LocalName == "PackageReference"
                                                                     || e.Name.LocalName == "ProjectReference" ) )
                             .Select( e => (Origin: e,
                                            PackageId: (string)e.Attribute( "Include" ),
                                            RawVersion: (string)e.Attribute( "Version" )) );

            var conditionEvaluator = new PartialEvaluator();
            var deps = new List<DeclaredPackageDependency>();
            var uselessDeps = new List<XElement>();
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
                    if( frameworks.IsEmpty )
                    {
                        m.Warn( $"Useless PackageReference (applies to undeclared frameworks): {p.Origin}." );
                        uselessDeps.Add( p.Origin );
                    }
                    else deps.Add( new DeclaredPackageDependency( this, p.PackageId, version, p.Origin, propertyDef, frameworks ) );
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
                    if( frameworks.IsEmpty )
                    {
                        m.Warn( $"Useless ProjectReference (applies to undeclared frameworks): {p.Origin}." );
                        uselessDeps.Add( p.Origin );
                    }
                    else projs.Add( new ProjectToProjectDependency( this, target, frameworks ) );
                }
            }
            _dependencies = new Dependencies( deps, projs, uselessDeps );
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
            var candidates = _file.AllFiles.Select( f => f.Document.Root )
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
