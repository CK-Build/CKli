using CK.Core;
using CK.Env;
using CK.Text;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CK.Env
{
    public static class CKSetupEnvLocalFeedExtension
    {
        public const string CKSetupStoreName = "CKSetupStore";

        public static string GetCKSetupStorePath( this IEnvLocalFeed @this )
        {
            return @this.PhysicalPath.AppendPart( CKSetupStoreName );
        }
    }
}
