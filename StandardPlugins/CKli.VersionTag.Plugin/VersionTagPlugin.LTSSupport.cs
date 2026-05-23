using CK.Core;
using CKli.Core;
using CSemVer;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata.Ecma335;

namespace CKli.VersionTag.Plugin;

public sealed partial class VersionTagPlugin
{
    /// <summary>
    /// Defines the configuration of a LTS World and the current default World. 
    /// </summary>
    /// <param name="Repo">The Repo.</param>
    /// <param name="LTSInfVersion">The InfVersion for this Repo in the LTS world.</param>
    /// <param name="LTSSupVersion">The SupVersion for this Repo in the LTS world.</param>
    public sealed record RepoLTSVersion( Repo Repo, SVersion? LTSInfVersion, SVersion LTSSupVersion )
    {
        /// <summary>
        /// Gets the future <see cref="VersionTagInfo.InfVersion"/> for this Repo in the default World
        /// </summary>
        public SVersion NextInfVersion => LTSSupVersion;
    }

    /// <summary>
    /// Computes the <see cref="RepoLTSVersion"/> that must be used to configure a new LTS World (and the <see cref="VersionTagInfo.InfVersion"/>
    /// of the current World.
    /// <para>
    /// This can only be called on the default World.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <returns>The <see cref="RepoLTSVersion"/> indexed by <see cref="Repo.Index"/>.</returns>
    public RepoLTSVersion[]? ComputeRepoLTSVersions( IActivityMonitor monitor )
    {
        Throw.CheckState( World.Name.IsDefaultWorld );

        if( !TryGetAllWithoutIssue( monitor, out var allVersions, "creating a LTS World" ) )
        {
            return null;
        }
        var result = new RepoLTSVersion[allVersions.Length];
        foreach( var versions in allVersions )
        {
            var lastProduced = versions.TagCommits.Keys.Max() ?? versions.InfVersion ?? SVersion.ZeroVersion;
            var cut = SVersion.Create( lastProduced.Major + 1, 0, 0, "0" );
            result[versions.Repo.Index] = new RepoLTSVersion( versions.Repo, versions.InfVersion, cut );
        }
        return result;
    }

}
