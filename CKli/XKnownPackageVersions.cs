using CK.Core;
using CK.Env;
using CK.Env.Analysis;
using CK.Env.MSBuild;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CKli
{
    [CK.Env.XName( "KnownPackageVersions" )]
    public class XKnownPackageVersionsIssuer : XIssuer
    {
        readonly XSolutionCentral _solutions;

        public class Constraint
        {
            /// <summary>
            /// Gets the package name.
            /// </summary>
            public string Name { get; }

            /// <summary>
            /// Gets the minimal package version.
            /// </summary>
            public SVersion Version { get; }

            /// <summary>
            /// Gets whether no version greater than <see cref="Version"/> must exist.
            /// </summary>
            public bool IsExactVersion { get; }

            /// <summary>
            /// Gets optional framework restrictions: only specified frameworks will be considered unless
            /// none is specified.
            /// </summary>
            public CKTrait Frameworks { get; }

            internal Constraint( string name, string v, bool isExact, string frameworks )
            {
                if( String.IsNullOrWhiteSpace( name ) ) throw new ArgumentNullException( nameof( name ) );
                Name = name;
                Version = SVersion.Parse( v );
                IsExactVersion = isExact;
                Frameworks = MSBuildContext.ParseSemiColonFrameworks( frameworks );
            }

            public override string ToString()
            {
                string f = Frameworks.IsEmpty ? "all target frameworks" : $"frameworks '{Frameworks}'";
                string c = IsExactVersion ? "" : "at least";
                return $"Package {Name} must be {c} {Version} in {f}";
            }
        }

        public XKnownPackageVersionsIssuer(
            XSolutionCentral solutions,
            IssueCollector collector,
            Initializer initializer )
            : base( collector, initializer )
        {
            _solutions = solutions;
            _constraints = initializer.Element.Elements( "Package" )
                            .Select( e => new Constraint( (string)e.AttributeRequired("Name"),
                                                          (string)e.Attribute( "Version" ) ?? (string)e.AttributeRequired( "MinVersion" ),
                                                          e.Attribute( "MinVersion" ) == null,
                                                          (string)e.Attribute( "Framework" ) ?? (string)e.Attribute( "Frameworks" )) )
                            .GroupBy( c => c.Name )
                            .ToDictionary( g => g.Key, g => g.ToArray() );
        }

        readonly Dictionary<string, Constraint[]> _constraints;

        public IEnumerable<Constraint> Constraints => _constraints.Values.SelectMany( c => c );

        public Constraint FindConstraint( string packageId, SVersion v, CKTrait f )
        {
            if( packageId == null ) throw new ArgumentNullException( nameof( packageId ) );
            if( v == null ) throw new ArgumentNullException( nameof( v ) );
            if( f == null ) throw new ArgumentNullException( nameof( f ) );
            if( _constraints.TryGetValue( packageId, out Constraint[] constraints ) )
            {
                foreach( var c in constraints )
                {
                    if( c.Frameworks.IsEmpty || c.Frameworks.Intersect( f ) == f )
                    {
                        return v < c.Version || (v != c.Version && c.IsExactVersion)
                                ? c
                                : null;
                    }
                }
            }
            return null;
        }

        protected override bool CreateIssue( IRunContextIssue builder )
        {
            var toFix = _solutions.AllSolutions.Select( s => s.GetSolution( builder.Monitor, false ) )
                              .SelectMany( s => s.AllProjects )
                              .SelectMany( p => p.Deps.Packages )
                              .Select( d => (D: d, C: FindConstraint( d.PackageId, d.Version, d.Frameworks )) )
                              .Where( t => t.C != null )
                              .ToList();
            if( toFix.Count > 0 )
            {
                foreach( var f in toFix.GroupBy( v => v.D.Owner ) )
                {
                    using( builder.Monitor.OpenInfo( $"Package {f.Key}" ) )
                    {
                        foreach( var c in f.Select( x => x.C ) )
                        {
                            builder.Monitor.Info( c.ToString() );
                        }
                    }
                    builder.CreateIssue( $"", $"Fixing Known package versions in {f.Key}.", m =>
                    {
                        foreach( var fix in f )
                        {
                            f.Key.SetPackageReferenceVersion( m, fix.D.Frameworks, fix.D.PackageId, fix.C.Version );
                            f.Key.Save( m, _solutions.MSBuildContext.FileSystem );
                        }
                        return f.Key.Save( m, _solutions.MSBuildContext.FileSystem );
                    } );
                }

            }
            return true;
        }
    }
}
