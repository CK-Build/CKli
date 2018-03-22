using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env.MSBuild
{
    /// <summary>
    /// Defines special type for a <see cref="Solution"/>.
    /// </summary>
    public enum SolutionSpecialType
    {
        /// <summary>
        /// No special type.
        /// </summary>
        None,

        /// <summary>
        /// Consider this secondary solution as an independent one: the primary
        /// solution is no more its container when analysing solutions, this secondary solution
        /// appears in the globally ordered list of solutions as if it was a primary one.
        /// </summary>
        IndependantSecondarySolution,

        /// <summary>
        /// Consider this secondary solution as "transparent": its projects must be logically attached
        /// to the primary solution and this secondary solution itself does not appear at all.
        /// Dependencies from projects from this secondary solution to any Package published by its
        /// primary solutions are considered as requirements to the origin Project itself.
        /// </summary>
        IncludedSecondarySolution
    }
}
