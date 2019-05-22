using CK.Core;

namespace CK.Env
{
    public interface IBasicApplicationLifetime
    {

        bool StopRequested( IActivityMonitor m );

        bool CanCancelStopRequest { get; }

        void CancelStopRequest();
    }
}
