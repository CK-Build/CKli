using CK.Core;
using CK.Build;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CK.Env.DependencyModel
{
    /// <summary>
    /// Simple model for a dependent solution with its direct, minimal and transitive requirements and impacts.
    /// </summary>
    public class DependentSolution
    {
        /// <summary>
        /// Expanded data that captures for each solution, all referenced projects (<see cref="Target"/>) to
        /// other solutions from its own project or solution (<see cref="Origin)"/>.
        /// </summary>
        public readonly struct Row
        {
            /// <summary>
            /// Gets the origin project or solution.
            /// It references the package produced by the <see cref="Target"/> project.
            /// Target is null if this origin is a ISolution without any dependency to other solutions.
            /// </summary>
            public IPackageReferrer Origin { get; }

            /// <summary>
            /// Gets the targeted project by <see cref="Origin"/>.
            /// Null if <see cref="Origin"/> is a solution that does not require any other solution.
            /// </summary>
            public IProject Target { get; }

            /// <summary>
            /// Gets the actual package references from <see cref="Origin"/> to <see cref="Target"/>.
            /// Null if <see cref="Origin"/> is a Solution does not require any other solution.
            /// </summary>
            public IEnumerable<ArtifactInstance> GetReferences()
            {
                var capture = Target;
                if( capture == null ) return null;
                if( Origin is IProject p )
                {
                    return p.PackageReferences
                                    .Where( d => capture.GeneratedArtifacts.Select( g => g.Artifact ).Contains( d.Target.Artifact ) )
                                    .Select( d => d.Target );
                }
                return Origin.Solution.SolutionPackageReferences
                        .Where( d => capture.GeneratedArtifacts.Select( g => g.Artifact ).Contains( d.Target.Artifact ) )
                        .Select( d => d.Target );
            }

            internal Row( IPackageReferrer o, IProject t )
            {
                Origin = o;
                Target = t;
            }

            public override string ToString() => $"{Origin.Name} => {Target?.Name ?? "<no dependency>"}";
        }

        internal DependentSolution(
            ISolution s,
            IReadOnlyList<Row> dependencyTable,
            Func<ISolution, DependentSolution> others )
        {
            Solution = s;
            Requirements = dependencyTable.Where( r => r.Origin.Solution == s && r.Target != null )
                            .Select( r => r.Target.Solution )
                            .Distinct()
                            .Select( others )
                            .ToList();

            MinimalRequirements = Requirements.Except( Requirements.SelectMany( r => r.MinimalRequirements ) ).ToList();
            Rank = MinimalRequirements.Count == 0 ? 0 : MinimalRequirements.Max( r => r.Rank ) + 1;
            TransitiveRequirements = Requirements.SelectMany( r => r.TransitiveRequirements ).Distinct().ToList();

            PublishedRequirements = dependencyTable.Where( r => r.Origin.Solution == s
                                              && r.Target != null
                                              && r.Origin is IProject oP
                                              && oP.IsPublished )
                            .Select( r => r.Target.Solution )
                            .Distinct()
                            .Select( others )
                            .ToList();
        }

        /// <summary>
        /// Gets the rank of this solution.
        /// </summary>
        public int Rank { get; }

        /// <summary>
        /// Gets the index of this solution.
        /// </summary>
        public int Index { get; internal set; }

        /// <summary>
        /// Gets the solution itself.
        /// </summary>
        public ISolution Solution { get; }

        /// <summary>
        /// Gets the direct required solutions: this corresponds to the solutions that generate a package
        /// used in this <see cref="Solution"/>.
        /// </summary>
        public IReadOnlyList<DependentSolution> Requirements { get; }

        /// <summary>
        /// Gets the direct required solutions for the published packages of this solution:
        /// this corresponds to the solutions that generate a package used by a published package in
        /// this <see cref="Solution"/>.
        /// </summary>
        public IReadOnlyList<DependentSolution> PublishedRequirements { get; }

        /// <summary>
        /// Gets the minimal set of required solutions: this is the sub set of the <see cref="Requirements"/>
        /// from which solutions that depend from other required solutions are removed.
        /// </summary>
        public IReadOnlyList<DependentSolution> MinimalRequirements { get; }

        /// <summary>
        /// Gets the maximal set of required solutions (the transitive closure of this <see cref="Requirements"/>).
        /// </summary>
        public IReadOnlyList<DependentSolution> TransitiveRequirements { get; }

        /// <summary>
        /// Gets the direct impacts of this solution: these are all the solutions that use at least one package
        /// generated by this <see cref="Solution"/>.
        /// </summary>
        public IReadOnlyList<DependentSolution> Impacts { get; private set; }

        /// <summary>
        /// Gets the minimal set of impacted solutions: this is the sub set of the <see cref="Impacts"/>
        /// from which solutions that impact any other impacted solutions are removed.
        /// </summary>
        public IReadOnlyList<DependentSolution> MinimalImpacts { get; private set; }

        /// <summary>
        /// Gets the maximal set of impacted solutions (the transitive closure of this <see cref="Impacts"/>).
        /// </summary>
        public IReadOnlyCollection<DependentSolution> TransitiveImpacts { get; private set; }

        /// <summary>
        /// Gets the locally produced packages that are consumed by this solution.
        /// Only packages that are produced by <see cref="Requirements"/> are considered.
        /// </summary>
        public IReadOnlyCollection<ImportedLocalPackage> ImportedLocalPackages { get; private set; }

        /// <summary>
        /// Gets the packages produced by this solution that are consumed by other solutions.
        /// Only packages that are used by another solution in the current context are in this set.
        /// To get the set of packages produced, use <see cref="ISolution.GeneratedArtifacts"/> (where
        /// <see cref="ArtifactType.IsInstallable"/> is true).
        /// </summary>
        public IReadOnlyCollection<ExportedLocalPackage> ExportedLocalPackages { get; private set; }

        /// <summary>
        /// Gets the global <see cref="SolutionDependencyContext"/> to which this <see cref="DependentSolution"/> belongs.
        /// </summary>
        public SolutionDependencyContext Solutions { get; private set; }

        internal void Initialize( SolutionDependencyContext solutions )
        {
            Solutions = solutions;
            Impacts = solutions.DependencyTable.Where( r => r.Target != null && r.Target.Solution == Solution )
                                            .Select( r => r.Origin.Solution )
                                            .Distinct()
                                            .Select( i => solutions.Solutions.First( d => d.Solution == i ) )
                                            .ToList();
            MinimalImpacts = Impacts.Except( Impacts.SelectMany( r => r.TransitiveImpacts ) ).ToList();
            var transitive = new HashSet<DependentSolution>( Impacts );
            foreach( var i in Impacts.SelectMany( r => r.TransitiveImpacts ) ) transitive.Add( i );
            TransitiveImpacts = transitive;

            ImportedLocalPackages = solutions.PackageDependencies
                                     .Where( d => d.Origin == this )
                                     .Select( d => new ImportedLocalPackage( d ) )
                                     .ToArray();

            ExportedLocalPackages = solutions.PackageDependencies
                                     .Where( d => d.Target == this )
                                     .Select( d => new ExportedLocalPackage( d ) )
                                     .ToArray();
        }

        public override string ToString() => Solution.ToString();

    }
}
