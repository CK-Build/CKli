using CK.Core;
using System.Xml.Linq;

namespace CK.Env.MSBuildSln
{
    /// <summary>
    /// Models a dependency from a <see cref="Project"/> to a <see cref="Project"/> (ProjectReference).
    /// This dependency can be defined at the solution level. In such case it is not bound to a <see cref="Owner"/>
    /// project and has no <see cref="Element"/>.
    /// </summary>
    public class ProjectToProjectDependency
    {
        internal ProjectToProjectDependency( Project o, Project target, CKTrait frameworks, XElement e )
        {
            Owner = o;
            TargetProject = target;
            Frameworks = frameworks;
            Element = e;
        }

        /// <summary>
        /// Gets whether this dependency is defined by the solution.
        /// </summary>
        public bool IsSolutionLevelDependency => Owner == null;

        /// <summary>
        /// Gets the project that owns this dependency.
        /// Null for a solution level dependency.
        /// </summary>
        public Project Owner { get; }

        /// <summary>
        /// Gets the referenced project.
        /// It necessarily belongs to the same <see cref="ProjectBase.Solution"/>
        /// as the <see cref="Owner"/>.
        /// </summary>
        public Project TargetProject { get; }

        /// <summary>
        /// Gets the frameworks to which this dependency applies.
        /// </summary>
        public CKTrait Frameworks { get; }

        /// <summary>
        /// Gets the ProjectReference element.
        /// Null for a solution level dependency.
        /// </summary>
        public XElement Element { get; }

    }
}
