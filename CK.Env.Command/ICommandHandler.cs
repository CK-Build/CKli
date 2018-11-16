using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    public interface ICommandHandler
    {
        NormalizedPath UniqueName { get; }

        bool GetEnabled();

        Type PayloadType { get; }

        object CreatePayload();

        void Execute( IActivityMonitor m, object payload );
    }
}
