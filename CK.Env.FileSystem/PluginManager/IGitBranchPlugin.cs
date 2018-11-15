using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    /// <summary>
    /// Marker interface.
    /// The single public constructor can have a parameter 'string branchName'.
    /// </summary>
    public interface IGitBranchPlugin : IGitPlugin
    {
    }
}
