using CK.Core;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Extends the passive <see cref="IScreen"/>.
/// </summary>
public interface IInteractiveScreen : IScreen
{
    /// <summary>
    /// Displays the current context and handles a command line input.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="context">The execution context.</param>
    /// <returns>The command line or null on "exit".</returns>
    Task<CommandLineArguments?> PromptAsync( IActivityMonitor monitor, CKliEnv context );
}
