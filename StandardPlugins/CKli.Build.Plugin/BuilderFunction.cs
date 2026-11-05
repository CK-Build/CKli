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
/// <para>
/// The working folder is checked out on the <paramref name="buildCommit"/> content: the function builds, tests
/// and packs it. It must leave the working folder as the build left it (the build plugin detects the impacts of
/// the build, then resets the working folder), must not publish the packages nor tag the version: the build plugin
/// does this from the returned <see cref="BuildOutput"/>.
/// </para>
/// </summary>
/// <param name="monitor">The monitor.</param>
/// <param name="context">The minimal CKli context.</param>
/// <param name="versionInfo">The version related info of the repository to build.</param>
/// <param name="buildCommit">The commit to build.</param>
/// <param name="runTest">Whether the repository's tests must be run.</param>
/// <param name="repoBuilder">The actual repository builder that will be used.</param>
/// <param name="buildInfo">The <see cref="CommitBuildInfo"/> that has been successfully computed.</param>
/// <param name="cancellation">The cancellation token.</param>
/// <returns>The build output on success, null on error.</returns>
public delegate Task<BuildOutput?> BuilderFunction( IActivityMonitor monitor,
                                                    CKliEnv context,
                                                    VersionTagInfo versionInfo,
                                                    Commit buildCommit,
                                                    bool runTest,
                                                    RepoBuilder repoBuilder,
                                                    CommitBuildInfo buildInfo,
                                                    CancellationToken cancellation );
