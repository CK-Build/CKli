namespace CKli.Core;

/// <summary>
/// Describes <see cref="Command.InteractiveMode"/>.
/// </summary>
public enum InteractiveMode
{
    /// <summary>
    /// The command can be executed in both contexts.
    /// </summary>
    Both,

    /// <summary>
    /// The command requires the interactive mode.
    /// </summary>
    Requires,

    /// <summary>
    /// The command is not interactive.
    /// </summary>
    Rejects
}
