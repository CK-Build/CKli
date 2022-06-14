using System;

namespace CK.Build
{
    [Flags]
    public enum PackageState : byte
    {
        None = 0,

        Unlisted = 1,

        Deprecated = 2,

        Ghost = 4
    }
}
