using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env.NPM
{
    public enum NPMProjectStatus
    {
        FatalInitializationError,
        ErrorMissingPackageJson,
        ErrorPackageMustBePrivate,
        ErrorPackageMustNotBePrivate,
        ErrorPackageNameMissing,
        ErrorPackageInvalidName,
        Valid,
    }
}
