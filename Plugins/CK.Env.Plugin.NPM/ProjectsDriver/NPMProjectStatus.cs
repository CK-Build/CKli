namespace CK.Env.Plugin
{
    /// <summary>
    /// Defines <see cref="Valid"/> state and
    /// multiple error states.
    /// </summary>
    public enum NPMProjectStatus
    {
        Valid,
        FatalInitializationError,
        ErrorMissingPackageJson,
        ErrorPackageMustBePrivate,
        ErrorPackageMustNotBePrivate,
        ErrorPackageNameMissing,
        ErrorPackageInvalidName,
        ErrorInvalidDependencyRecord,
        ErrorInvalidDependencyVersion,
    }
}
