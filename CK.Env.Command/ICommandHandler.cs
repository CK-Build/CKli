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

        bool HasPayload { get; }

        object CreatePayload();

        void Handle( IActivityMonitor m, object payload );
    }
}
