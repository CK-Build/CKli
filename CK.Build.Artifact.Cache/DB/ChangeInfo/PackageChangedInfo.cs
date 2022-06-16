using CK.Core;

namespace CK.Build.PackageDB
{
    /// <summary>
    /// Describes a change in one package.
    /// </summary>
    public readonly struct PackageChangedInfo
    {
        /// <summary>
        /// The type of change.
        /// </summary>
        public PackageEventType ChangeType { get; }

        /// <summary>
        /// The package that changed.
        /// </summary>
        public PackageInstance Package { get; }

        internal PackageChangedInfo( PackageEventType type, PackageInstance package )
        {
            Throw.CheckNotNullArgument( package );
            ChangeType = type;
            Package = package;
        }
    }

}

