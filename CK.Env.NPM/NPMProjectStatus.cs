using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env.NPM
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
