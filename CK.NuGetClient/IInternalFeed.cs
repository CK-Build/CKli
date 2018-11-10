using NuGet.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.NuGetClient
{
    interface IInternalFeed : INuGetFeed
    {
        void CollectPackageSources( List<PackageSource> collector );
    }
}
