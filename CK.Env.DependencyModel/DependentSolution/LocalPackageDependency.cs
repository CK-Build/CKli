using CK.Core;
using System.Collections.Generic;
using System.Diagnostics;

namespace CK.Env.DependencyModel
{
    /// <summary>
    /// Captures a dependency description between one project from a <see cref="DependentSolution"/> (the <see cref="Origin"/>)
    /// to a project produced by another solution (the <see cref="Target"/>).
    /// </summary>
    public class LocalPackageDependency
    {
        readonly DependentSolution.Row _row;
        readonly ArtifactInstance _reference;

        internal LocalPackageDependency( DependentSolution.Row row, ArtifactInstance r, Dictionary<object, DependentSolution> index )
        {
            _row = row;
            _reference = r;
            Origin = index[row.Origin.Solution];
            Target = index[row.Target.Solution];
        }

        /// <summary>
        /// Gets the dependent solution that references the <see cref="TargetProject"/>'s artifact.
        /// </summary>
        public DependentSolution Origin { get; }

        /// <summary>
        /// Gets the <see cref="IProject"/> or <see cref="ISolution"/> that references the <see cref="TargetProject"/>'s artifact.
        /// </summary>
        public IPackageReferer RefererOrigin => _row.Origin;

        /// <summary>
        /// Gets whether the <see cref="RefererOrigin"/> is a <see cref="IProject"/> that has a true <see cref="IProject.IsPublished"/>.
        /// </summary>
        public bool OriginIsPublishedProject => _row.Origin is IProject p && p.IsPublished;

        /// <summary>
        /// Gets the dependent solution that produces the artifact.
        /// </summary>
        public DependentSolution Target { get; }

        /// <summary>
        /// Gets the target project, the one that generates the artifact.
        /// </summary>
        public IProject TargetProject => _row.Target;

        /// <summary>
        /// Gets the artifact instance that is consumed by <see cref="OriginProject"/> and produced by <see cref="TargetProject"/>.
        /// </summary>
        public ArtifactInstance Reference => _reference;

        /// <summary>
        /// Overridden to return the un
        /// </summary>
        /// <returns></returns>
        public override string ToString() => $"{_row.Origin.Name} => {_reference}";
    }

}
