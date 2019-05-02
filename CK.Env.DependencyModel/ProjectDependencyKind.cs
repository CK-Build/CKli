using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    /// <summary>
    /// Defines how a project references another project or package.
    /// </summary>
    public enum ProjectDependencyKind
    {
        /// <summary>
        /// Non applicable.
        /// </summary>
        None,

        /// <summary>
        /// Non transitive dependency.
        /// </summary>
        Private,

        /// <summary>
        /// Development only dependency. No transitivity at all.
        /// </summary>
        Development,

        /// <summary>
        /// "Normal", standard, transitive dependency.
        /// This is the "Peer dependency" of NPM, see https://lexi-lambda.github.io/blog/2016/08/24/understanding-the-npm-dependency-model/ 
        /// </summary>
        Transitive
    }
    
}
