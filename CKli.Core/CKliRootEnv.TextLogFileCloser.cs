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

    /// <summary>
    /// Closes the current "Text/" log file and returns its final path (the file is renamed from ".log.tmp" to ".log"
    /// when it is closed).
    /// </summary>
    sealed class TextLogFileRoller : GrandOutputHandlersAction<string?>
    {
        protected override ValueTask<string?> RunAsync( IActivityMonitor monitor, DispatcherSink.HandlerList handlers )
        {
            foreach( var h in handlers.Handlers )
            {
                if( h is CK.Monitoring.Handlers.TextFile t && t.KeyPath == "Text" )
                {
                    return ValueTask.FromResult( t.CloseCurrentFile( forgetCurrentFile: false ) );
                }
            }
            return ValueTask.FromResult<string?>( null );
        }
    }

    /// <summary>
    /// Closes the current text log file so that it can be opened right now and returns its path.
    /// The following logs go to a new file.
    /// <para>
    /// This is used to point at the log file that contains a failure (typically the output of a failed
    /// "dotnet build" or "dotnet test"): the entries logged before this call are in the returned file.
    /// </para>
    /// </summary>
    /// <returns>The path of the closed log file or null if there is no current text log file.</returns>
    public static string? CloseCurrentTextLogFile()
    {
        var go = GrandOutput.Default;
        if( go == null ) return null;
        var roller = new TextLogFileRoller();
        go.Sink.Submit( roller );
        // The dispatcher runs on its own loop: this cannot deadlock but we don't want to block forever.
        return roller.Completion.Wait( 5000 ) ? roller.Completion.Result : null;
    }
}
