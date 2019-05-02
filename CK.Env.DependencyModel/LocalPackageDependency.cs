using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
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
            Origin = index[row.Origin];
            Target = index[row.Target];
        }

        /// <summary>
        /// Gets the dependent solution that references the <see cref="TargetProject"/>'s artifact.
        /// </summary>
        public DependentSolution Origin { get; }

        /// <summary>
        /// Gets the project that references the <see cref="TargetProject"/>'s artifact.
        /// </summary>
        public DependentProject OriginProject => _row.Origin;

        /// <summary>
        /// Gets the dependent solution that produces the artifact.
        /// </summary>
        public DependentSolution Target { get; }

        /// <summary>
        /// Gets the target project that generate the artifact.
        /// </summary>
        public DependentProject TargetProject => _row.Target;

        /// <summary>
        /// Gets the artifact instance that is consumed by <see cref="OriginProject"/> and produced by <see cref="TargetProject"/>.
        /// </summary>
        public ArtifactInstance Reference => _reference;
    }

}
