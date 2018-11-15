using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    /// <summary>
    /// No constraint: simple marker interface.
    /// There must be one and only one public constructor that must not have a 'string branchName' parameter.
    /// </summary>
    public interface IGitPlugin
    {
    }
}
