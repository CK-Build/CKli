using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env.NPM
{
    /// <summary>
    /// Categorizes the kind of NPM dependency.
    /// </summary>
    public enum DependencyKind
    {
        /// <summary>
        /// Non applicable.
        /// </summary>
        None,

        /// <summary>
        /// Normal (private) dependencies.
        /// </summary>
        Normal,

        /// <summary>
        /// Development only dependency.
        /// No transitivity at all.
        /// </summary>
        Dev,

        /// <summary>
        /// Peer dependencies: "normal", standard transitive dependencies.
        /// https://lexi-lambda.github.io/blog/2016/08/24/understanding-the-npm-dependency-model/ 
        /// </summary>
        Peer
    }
}
