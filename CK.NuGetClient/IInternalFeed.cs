using NuGet.Configuration;
using System.Collections.Generic;

namespace CK.NuGetClient
{
    interface IInternalFeed : INuGetFeed
    {
        void CollectPackageSources( List<PackageSource> collector );
    }
}
