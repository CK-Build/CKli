namespace CKli.Core;

/// <summary>
/// User preferences are independent of the stacks.
/// </summary>
public interface IUserPreferences
{
    /// <summary>
    /// Get the secrets store to use.
    /// </summary>
    ISecretsStore SecretsStore { get; }
}
