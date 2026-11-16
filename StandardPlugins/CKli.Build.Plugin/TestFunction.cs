using CK.Core;
using CKli.Core;
using CKli.VersionTag.Plugin;
using LibGit2Sharp;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Build.Plugin;

/// <summary>
/// Test function called when the tests of an already built release must run (see
/// <see cref="BuildPlugin.EnsureTestedAsync"/>). See the static <see cref="BuildPlugin.SetTestFunction(TestFunction?)"/>.
/// <para>
/// The working folder is checked out on the <paramref name="testCommit"/> content: the function builds it and runs
/// its tests. It must not pack nor generate the deployment assets: the release already has its artifacts and
/// they must not be touched. The build plugin records the tested content and resets the working folder.
/// </para>
/// </summary>
/// <param name="monitor">The monitor.</param>
/// <param name="context">The minimal CKli context.</param>
/// <param name="versionInfo">The version related info of the repository.</param>
/// <param name="testCommit">The commit of the release.</param>
/// <param name="repoBuilder">The actual repository builder that will be used.</param>
/// <param name="buildInfo">The <see cref="CommitBuildInfo"/> of the release.</param>
/// <param name="cancellation">The cancellation token.</param>
/// <returns>True if the tests succeeded, false otherwise.</returns>
public delegate Task<bool> TestFunction( IActivityMonitor monitor,
                                         CKliEnv context,
                                         VersionTagInfo versionInfo,
                                         Commit testCommit,
                                         RepoBuilder repoBuilder,
                                         CommitBuildInfo buildInfo,
                                         CancellationToken cancellation );
