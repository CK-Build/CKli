using CK.Core;
using CK.Env;
using CK.Env.Analysis;
using CK.Env.MSBuild;
using CK.Text;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CKli
{
    [CK.Env.XName( "KnownPackageDependencies" )]
    public class XKnownPackageDependenciesIssuer : XIssuer
    {
        readonly XSolutionCentral _solutions;

        public class TransitiveDependency
        {
            /// <summary>
            /// Gets the source package name.
            /// </summary>
            public string Name { get; }

            /// <summary>
            /// Gets optional framework restrictions: only specified frameworks will be considered unless
            /// none is specified.
            /// </summary>
            public CKTrait Frameworks { get; }

            /// <summary>
            /// Gets the packages that are implied by <see cref="Name."/>
            /// </summary>
            public IReadOnlyList<string> Requires { get; }

            internal TransitiveDependency( string name, string frameworks, string requires )
            {
                if( String.IsNullOrWhiteSpace( name ) ) throw new ArgumentNullException( nameof( name ) );
                Name = name;
                Frameworks = MSBuildContext.ParseSemiColonFrameworks( frameworks );
                Requires = requires?.Split( new[] { ';' }, StringSplitOptions.RemoveEmptyEntries );
                if( Requires == null || Requires.Count == 0 ) throw new ArgumentException( "Must specify at leas one package name.", nameof( requires ) );
            }

            public override string ToString()
            {
                string f = Frameworks.IsEmpty ? "all target frameworks" : $"frameworks '{Frameworks}'";
                return $"Package {Name} requires {Requires.Concatenate()} in {f}";
            }
        }

        public XKnownPackageDependenciesIssuer(
            XSolutionCentral solutions,
            IssueCollector collector,
            Initializer initializer )
            : base( collector, initializer )
        {
            _solutions = solutions;
            _knownDeps = initializer.Element.Elements( "Package" )
                            .Select( e => new TransitiveDependency(
                                                          (string)e.AttributeRequired("Name"),
                                                          (string)e.Attribute( "Framework" ) ?? (string)e.Attribute( "Frameworks" ),
                                                          (string)e.AttributeRequired( "Requires" ) ) )
                            .ToArray();
        }

        readonly TransitiveDependency[] _knownDeps;

        public IReadOnlyCollection<TransitiveDependency> TransitiveDependencies => _knownDeps;

        protected override bool CreateIssue( IRunContextIssue builder )
        {
            var toRemove = _solutions.AllDevelopSolutions.Select( s => s.GetSolution( builder.Monitor, false ) )
                        .SelectMany( s => s.AllProjects )
                        .Select( p => (Project: p,
                                       Applicable: _knownDeps.Where( dep => p.Deps.GetFilteredPackageReferences( dep.Frameworks )
                                                                                  .Where( d => dep.Name == d.PackageId ).Any() )) )
                        .Select( t => (t.Project,
                                       ToRemove: t.Project.Deps.Packages
                                                    .Where( d => t.Applicable.Any( a =>
                                                                    d.Frameworks.Intersect( a.Frameworks ) == a.Frameworks
                                                                    && a.Requires.Contains( d.PackageId ) ) )
                                                    .ToList()) )
                        .Where( t => t.ToRemove.Count > 0 )
                        .ToList();
            if( toRemove.Count > 0 )
            {
                foreach( var t in toRemove )
                {
                    using( builder.Monitor.OpenInfo( $"{t.ToRemove.Count} transitive dependencies in {t.Project} can be removed." ) )
                    {
                        foreach( var dep in t.ToRemove )
                        {
                            builder.Monitor.Info( dep.ToString() );
                        }
                    }
                }
                builder.CreateIssue( "RemovingTransitiveDependencies", $"{toRemove.Count} projects contain transitive dependencis.", m =>
                {
                    foreach( var r in toRemove )
                    {
                        r.Project.RemoveDependencies( m, r.ToRemove );
                        if( !r.Project.Save( m, _solutions.MSBuildContext.FileSystem ) ) return false;
                    }
                    return true;
                } );
            }

            return true;
        }
    }
}
