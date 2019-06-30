using CK.Core;
using CK.Env;

namespace CKli
{
    class FakeApplicationLifetime : IBasicApplicationLifetime
    {
        public FakeApplicationLifetime()
        {
        }
        public bool CanCancelStopRequest => false;

        public void CancelStopRequest()
        {
        }

        public bool StopRequested( IActivityMonitor m )
        {
            return false;
        }
    }
}
