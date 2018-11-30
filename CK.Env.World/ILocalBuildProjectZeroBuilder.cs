using CK.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    /// <summary>
    ///  
    /// </summary>
    public interface ILocalBuildProjectZeroBuilder
    {
        /// <summary>
        /// Must generate the build projects in Zero version and makes any
        /// reference to them use the ZeroVersion package.
        /// </summary>
        /// <param name="m"></param>
        /// <param name="r"></param>
        /// <returns></returns>
        bool LocalZeroBuildProjects( IActivityMonitor m, IDependentSolutionContext r );
    }
}
