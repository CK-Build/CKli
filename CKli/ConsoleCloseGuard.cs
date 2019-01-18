using CK.Core;
using CK.Env;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CKli
{

    class ConsoleCloseGuard : IBasicApplicationLifetime
    {
        [DllImport( "Kernel32" )]
        private static extern bool SetConsoleCtrlHandler( EventHandler handler, bool add );

        private delegate bool EventHandler( CtrlType sig );
        static EventHandler _handler;

        enum CtrlType
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT = 1,
            CTRL_CLOSE_EVENT = 2,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT = 6
        }

        static bool Handler( CtrlType sig )
        {
            switch( sig )
            {
                case CtrlType.CTRL_C_EVENT:
                case CtrlType.CTRL_LOGOFF_EVENT:
                case CtrlType.CTRL_SHUTDOWN_EVENT:
                case CtrlType.CTRL_CLOSE_EVENT:
                    _stopRequest = true;
                    return false;
                default: return true;
            }
        }

        bool IBasicApplicationLifetime.StopRequested( IActivityMonitor m )
        {
            if( _stopRequest )
            {
                m.Warn( "Interrupted by user." );
                return true;
            }
            return false;
        }

        bool IBasicApplicationLifetime.CanCancelStopRequest => true;

        void IBasicApplicationLifetime.CancelStopRequest() =>_stopRequest = false;

        static bool _stopRequest;
        static ConsoleCloseGuard _instance;

        public static IBasicApplicationLifetime Default
        {
            get
            {
                if( _instance == null )
                {
                    _handler += new EventHandler( Handler );
                    SetConsoleCtrlHandler( _handler, true );
                    _instance = new ConsoleCloseGuard();
                }
                return _instance;
            }
        }
    }
}
