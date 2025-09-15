namespace CKli.Core;

public sealed class DefaultUserPreferences : IUserPreferences
{
    // Currently, the only implemented secret store id the DotNetUserSecretsStore.
    // Alternate stores may be implemented. Instances will need to be restored from a
    // stack configuration file that does not exist yet. A simple name should be used
    // to locate the type 
    static readonly ISecretsStore _defaultSecretStore = new DotNetUserSecretsStore();

    public ISecretsStore SecretsStore => _defaultSecretStore;
}
