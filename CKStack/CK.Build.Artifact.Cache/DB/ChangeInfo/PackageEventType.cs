namespace CK.Build.PackageDB
{
    /// <summary>
    /// Captures the <see cref="PackageChangedInfo.ChangeType"/>.
    /// </summary>
    public enum PackageEventType
    {
        /// <summary>
        /// No change.
        /// </summary>
        None,

        /// <summary>
        /// The package instance itself has changed.
        /// This is weird but handled.
        /// </summary>
        ContentOnlyChanged,

        /// <summary>
        /// The <see cref="PackageInstance.State"/> has changed.
        /// </summary>
        StateOnlyChanged,

        /// <summary>
        /// Both the content and the state of the package instance has changed.
        /// </summary>
        ContentAndStateChanged,

        /// <summary>
        /// The package instance is a new one.
        /// </summary>
        Added,

        /// <summary>
        /// The package instance has been destroyed.
        /// </summary>
        Destroyed
    }

}

