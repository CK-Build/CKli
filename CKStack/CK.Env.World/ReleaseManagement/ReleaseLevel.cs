namespace CK.Env
{
    /// <summary>
    /// Defines the 4 possible release level.
    /// This is composable (maximal value).
    /// </summary>
    public enum ReleaseLevel
    {
        /// <summary>
        /// No release required.
        /// Either it is already done or it should be canceled.
        /// </summary>
        None,

        /// <summary>
        /// Release is for fix only.
        /// </summary>
        Fix,

        /// <summary>
        /// Release introduces features.
        /// </summary>
        Feature,

        /// <summary>
        /// Release introduces breaking changes.
        /// </summary>
        BreakingChange
    }
}
