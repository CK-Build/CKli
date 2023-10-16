using CK.Core;

namespace CK.Env.MSBuildSln
{
    /// <summary>
    /// Stack local project are in "$Local" folder and are outside of the
    /// solution folder.
    /// </summary>
    public sealed class StackLocalProject : ProjectBase
    {
        internal StackLocalProject( SolutionFolder local,
                                    KnownProjectType type,
                                    string projectName,
                                    NormalizedPath relativePath )
            : base( local, type, projectName, relativePath )
        {
        }
    }
}
