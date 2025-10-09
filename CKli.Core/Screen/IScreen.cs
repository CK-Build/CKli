using System;
using System.Collections.Generic;

namespace CKli.Core;

public interface IScreen : IDisposable
{
    void DisplayError( string message );

    void DisplayWarning( string message );

    void OnLogText( string text );

    void DisplayHelp( List<CommandHelpBlock> commands, CommandLineArguments cmdLine );

}
