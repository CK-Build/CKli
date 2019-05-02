namespace CK.Env
{
    public enum SolutionSortStrategy
    {
        /// <summary>
        /// Consider only published projects of solutions.
        /// </summary>
        PublishedProjects = 1,

        /// <summary>
        /// Consider published and tests projects of solutions.
        /// </summary>
        PublishedAndTestsProjects = 2,

        /// <summary>
        /// Consider all projects.
        /// Build projects are ignored.
        /// </summary>
        EverythingExceptBuildProjects = 3
    }
}
