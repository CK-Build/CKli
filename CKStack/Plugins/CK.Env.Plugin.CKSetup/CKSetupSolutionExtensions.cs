using CK.Env.DependencyModel;

namespace CK.Env
{
    /// <summary>
    /// Extends <see cref="ISolution"/> with a UseCKSetup flag.
    /// </summary>
    public static class CKSetupSolutionExtensions
    {
        internal sealed class Marker { }
        internal readonly static Marker _marker = new Marker();

        /// <summary>
        /// Gets whether the solution uses CKSetup components (a &lt;CKSetup /&gt; element appears
        /// in the RepositoryInfo.xml file.
        /// <para>
        /// When true, a CKSetupStore.txt file is created during build at the root of the solution so that one of the
        /// store in /LocalFeed folders is used instead of the default local (%UserProfile%AppData\Local\CKSetupStore).
        /// </para>
        /// <para>
        /// The &lt;CKSetup&gt; element can contain &lt;Component&gt;...&lt;/Component&gt; child element with paths
        /// to projects that are CKSetup components (models and engines). 
        /// </para>
        /// </summary>
        /// </summary>
        /// <param name="s">This solution.</param>
        /// <returns>True if this solution uses CKSetup, false otherwise.</returns>
        public static bool UseCKSetup( this ISolution s ) => s.Tag<Marker>() != null;
    }
}
