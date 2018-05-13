using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    public enum WorkStatus
    {
        Idle,
        SwitchingToLocal,
        SwitchingToDevelop,
        Releasing,
        WaitingReleaseConfirmation,
        CancellingRelease,
        PublishingRelease,
    }
}
