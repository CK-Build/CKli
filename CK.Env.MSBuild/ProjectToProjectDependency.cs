using CK.Core;
using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace CK.Env.MSBuild
{
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
        /// Gets the project that owns this dependency.
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
        /// </summary>
        public XElement Element { get; }

    }
}
