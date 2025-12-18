using CK.Core;
using CK.Monitoring;
using System.Threading.Tasks;

namespace CKli.Core;

public static partial class CKliRootEnv
{
    /// <summary>
    /// Closes the current "Text/" log file.
    /// </summary>
    /// <param name="forgetCurrentFile">True to forget the current logs.</param>
    /// <param name="deactivateTextHandler">True to remove the Text handler. Used at application exit to avoid "Stopping GrandOutput" useless logs.</param>
    sealed class TextLogFileCloser( bool forgetCurrentFile, bool deactivateTextHandler ) : GrandOutputHandlersAction
    {
        protected override async ValueTask RunAsync( IActivityMonitor monitor, DispatcherSink.HandlerList handlers )
        {
            foreach( var h in handlers.Handlers )
            {
                if( h is CK.Monitoring.Handlers.TextFile t && t.KeyPath == "Text" )
                {
                    t.CloseCurrentFile( forgetCurrentFile );
                    if( deactivateTextHandler ) await handlers.RemoveAsync( monitor, h ).ConfigureAwait( false );
                    break;
                }
            }
        }
    }

}
