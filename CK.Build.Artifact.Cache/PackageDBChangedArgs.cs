using System;

namespace CK.Build
{
    public class PackageDBChangedArgs : EventArgs
    {
        public PackageDBChangedArgs( PackageDB db )
        {
            PackageDB = db;
        }

        public PackageDB PackageDB { get; }
    }
}
