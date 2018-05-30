using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env.MSBuild
{
    /// <summary>
    /// Modelizes a <see cref="Project"/> and its <see cref="Framework"/>.
    /// To correctly analyse package dependencies we have to consider the target frameworks of each project.
    /// Different versions of the same package can live together as long as they are not both used by
    /// the same <see cref="IProjectFramework"/>
    /// </summary>
    public interface IProjectFramework
    {
        /// <summary>
        /// Gets the local project.
        /// </summary>
        IDependentProject Project { get; }

        /// <summary>
        /// Gets the atomic framework of this <see cref="IProjectFramework"/>.
        /// </summary>
        CKTrait Framework { get; }

        /// <summary>
        /// Gets the list of <see cref="IProjectFramework"/> that reference this project in this framework.
        /// </summary>
        IReadOnlyList<IProjectFramework> Refs { get; }

        /// <summary>
        /// Gets the maximum number of references from any root project.
        /// </summary>
        int Depth { get; }

    }


}
