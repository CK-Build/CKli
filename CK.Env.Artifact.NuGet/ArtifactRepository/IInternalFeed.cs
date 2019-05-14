using NuGet.Configuration;
using System.Collections.Generic;

namespace CK.Env.NuGet
{
    interface IInternalFeed : INuGetFeed
    {
        void CollectPackageSources( List<PackageSource> collector );
    }
}
