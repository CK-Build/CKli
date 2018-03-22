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
                var t = requires?.Split( new[] { ';' }, StringSplitOptions.RemoveEmptyEntries );
                if( t == null || t.Length == 0 ) throw new ArgumentException( "Must specify at leas one package name.", nameof( requires ) );
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

        public IEnumerable<TransitiveDependency> TransitiveDependencies => _knownDeps;

        protected override bool CreateIssue( IRunContextIssue builder )
        {
            return true;
        }
    }
}
