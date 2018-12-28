using System;
using System.Collections.Generic;
using System.Text;

namespace CKSetup
{
    public static class TODOCKSetupCoreExyension
    {
        /// <summary>
        /// Gets the "net461", "netcoreapp1.0" string representation as it appears in csproj files
        /// and output folders.
        /// </summary>
        /// <param name="f">This target framework.</param>
        /// <returns>The framework string.</returns>
        public static string ToStringFramework( this TargetFramework f )
        {
            switch( f )
            {
                case TargetFramework.Net451: return "net451";
                case TargetFramework.Net46: return "net46";
                case TargetFramework.Net461: return "net461";
                case TargetFramework.Net462: return "net462";
                case TargetFramework.Net47: return "net47";
                case TargetFramework.Net471: return "net471";
                case TargetFramework.Net472: return "net472";
                case TargetFramework.NetStandard10: return "netstandard1.0";
                case TargetFramework.NetStandard11: return "netstandard1.1";
                case TargetFramework.NetStandard12: return "netstandard1.2";
                case TargetFramework.NetStandard13: return "netstandard1.3";
                case TargetFramework.NetStandard14: return "netstandard1.4";
                case TargetFramework.NetStandard15: return "netstandard1.5";
                case TargetFramework.NetStandard16: return "netstandard1.6";
                case TargetFramework.NetStandard20: return "netstandard2.0";
                case TargetFramework.NetCoreApp10: return "netcoreapp1.0";
                case TargetFramework.NetCoreApp11: return "netcoreapp1.1";
                case TargetFramework.NetCoreApp20: return "netcoreapp2.0";
                case TargetFramework.NetCoreApp21: return "netcoreapp2.1";
                default: return null;
            }
        }

    }
}
