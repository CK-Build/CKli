namespace CKli.Core;

/// <summary>
/// Describes the returned type of method handlers.
/// </summary>
public enum MethodAsyncReturn
{
    /// <summary>
    /// A synchronous <c>bool</c>.
    /// </summary>
    None,

    /// <summary>
    /// A <c>ValueTask&lt;bool&gt;</c>.
    /// </summary>
    ValueTask,

    /// <summary>
    /// A <c>Task&lt;bool&gt;</c>.
    /// </summary>
    Task,
}
