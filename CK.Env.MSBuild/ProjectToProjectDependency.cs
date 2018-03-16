using CK.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env.MSBuild
{
    public class ProjectToProjectDependency
    {
        internal ProjectToProjectDependency( Project o, Project target, CKTrait frameworks )
        {
            Owner = o;
            Project = target;
            Frameworks = frameworks;
        }

        /// <summary>
        /// Gets the project that owns this dependency.
        /// </summary>
        public Project Owner { get; }

        /// <summary>
        /// Gets the referenced project.
        /// It necessarily belongs to the same <see cref="ProjectBase.Solution"/>
        /// as the <see cref="Owner"/>.
        /// </summary>
        public Project Project { get; }

        /// <summary>
        /// Gets the frameworks to which this dependency applies.
        /// </summary>
        public CKTrait Frameworks { get; }


    }
}
