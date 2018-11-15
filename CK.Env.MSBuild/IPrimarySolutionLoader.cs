using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env.MSBuild
{
    public interface IPrimarySolutionLoader
    {
        Solution GetSolution( IActivityMonitor m, bool reload );
    }
}
