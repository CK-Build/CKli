using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.VersionTag.Plugin;
using LibGit2Sharp;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Build.Plugin;

/// <summary>
/// Build function called when a build must be done.
/// See the static <see cref="BuildPlugin.SetBuilderFunction(CKli.Build.Plugin.BuilderFunction?)"/>.
/// </summary>
/// <param name="monitor">The monitor.</param>
/// <param name="context">The minimal CKli context.</param>
/// <param name="versionInfo">The version related info of the repository to build.</param>
/// <param name="buildCommit">The commit to build.</param>
/// <param name="runTest">Whether the repository's tests must be run.</param>
/// <param name="repoBuilder">The actual repository builder that will be used.</param>
/// <param name="buildInfo">The <see cref="CommitBuildInfo"/> that has been successfully computed.</param>
/// <param name="cancellation">The cancellation token.</param>
/// <returns>The build result on success, null on error.</returns>
public delegate Task<BuildResult?> BuilderFunction( IActivityMonitor monitor,
                                                    CKliEnv context,
                                                    VersionTagInfo versionInfo,
                                                    Commit buildCommit,
                                                    bool runTest,
                                                    RepoBuilder repoBuilder,
                                                    CommitBuildInfo buildInfo,
                                                    CancellationToken cancellation );
