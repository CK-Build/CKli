using CK.Core;
using CKli.BranchModel.Plugin;
using CKli.Core;
using CKli.VersionTag.Plugin;
using LibGit2Sharp;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CKli.Build.Plugin;

public sealed partial class BuildPlugin
{
    void IssueRequested( IssueEvent e )
    {
        var monitor = e.Monitor;
        foreach( var r in e.Repos )
        {
            CollectVersionTagIssues( monitor, _versionTags.Get( monitor, r ), e.ScreenType, e.Add ); 
        }
    }

    void CollectVersionTagIssues( IActivityMonitor monitor,
                                  VersionTagInfo versionTagInfo,
                                  ScreenType screenType,
                                  Action<World.Issue> collector )
    {

        // Tags rebuild case.
        // If there are tags to rebuild, then the "NoVersionTagIssue" is not (yet) relevant.
        var regulars = versionTagInfo.LightweightOrUnreadableRegularTags;
        if( regulars != null )
        {
            var lightWeightTags = regulars.Where( tc => !tc.T.IsAnnotated ).ToArray();
            const string rebuildMessage = """
            Fixing these tags recompiles the commit to obtain the consumed/produced packages and asset files.
            On success, the tag content is updated.
            When the commit cannot be successfully recompiled, the command 'ckli maintenance rebuild old'
            can try to build them and sets a "+invalid" tag on failure.
            """;
            if( lightWeightTags.Length > 0 )
            {
                collector( new TagsRebuildIssue( this,
                                                 versionTagInfo,
                                                 $"{lightWeightTags.Length} lightweight tags must be transformed to annotated tags.",
                                                 screenType.Text( $"""
                                                        {lightWeightTags.Select( vt => vt.V.ParsedText ).Concatenate()}

                                                        {rebuildMessage}
                                                        """ ),
                                                 lightWeightTags ) );
            }
            var unreadableMessages = regulars.Where( tc => tc.T.IsAnnotated ).ToArray();
            if( unreadableMessages.Length > 0 )
            {
                monitor.Info( $"""
                The {unreadableMessages.Length} following tags in '{versionTagInfo.Repo.DisplayPath}' have unreadable messages:
                {unreadableMessages.Select( vt => $"- {vt.V.ParsedText}:{Environment.NewLine}{vt.T.Annotation.Message}{Environment.NewLine}" ).Concatenate( Environment.NewLine )}
                """ );
                collector( new TagsRebuildIssue( this,
                                                 versionTagInfo,
                                                 $"{unreadableMessages.Length} tags have unreadable content info (see logs for details).",
                                                 screenType.Text( $"""
                                                        {unreadableMessages.Select( vt => vt.V.ParsedText ).Concatenate()}

                                                        {rebuildMessage}
                                                        """ ),
                                                 unreadableMessages ) );
            }
        }
        else
        {
            // No version tag case (only if there are no tags to rebuild).
            if( versionTagInfo.HotZone == null )
            {
                Throw.DebugAssert( versionTagInfo.HotZone == null );
                var branchModel = _branchModel.Get( monitor, versionTagInfo.Repo );
                if( branchModel.Root.GitBranch != null )
                {
                    // We have a root branch: let's fix this by building it based on the InfVersion.
                    var vBase = versionTagInfo.InfVersion ?? SVersion.ZeroVersion;
                    var vInit = $"v{vBase.Major}.{vBase.Minor}.{vBase.Patch}+fake";
                    collector( new NoVersionTagIssue( this,
                                                      versionTagInfo,
                                                      "Missing initial version.",
                                                      screenType.Text( $"""
                                                      This can be fixed by creating a '{vInit}' on '{branchModel.Root.BranchName}' branch.
                                                      """ ),
                                                      branchModel.Root,
                                                      vInit ) );
                }
            }
        }
    }

    sealed class TagsRebuildIssue : World.Issue
    {
        readonly BuildPlugin _buildPlugin;
        readonly VersionTagInfo _versionTagInfo;
        readonly (SVersion V, Tag T)[] _tagsToRebuild;

        public TagsRebuildIssue( BuildPlugin buildPlugin,
                                 VersionTagInfo versionTagInfo,
                                 string title,
                                 IRenderable body,
                                 (SVersion V, Tag T)[] tagsToRebuild )
            : base( title, body, versionTagInfo.Repo )
        {
            _buildPlugin = buildPlugin;
            _versionTagInfo = versionTagInfo;
            _tagsToRebuild = tagsToRebuild;
        }

        protected override async  ValueTask<bool> ExecuteAsync( IActivityMonitor monitor, CKliEnv context, World world )
        {
            Throw.DebugAssert( Repo != null );
            using var gLog = monitor.OpenInfo( $"Fixing {_tagsToRebuild.Length} tags content info in '{Repo.DisplayPath}'." );
            foreach( var (v,t) in _tagsToRebuild )
            {
                var buildResult = await _buildPlugin.CoreBuildAsync( monitor,
                                                                     context,
                                                                     _versionTagInfo,
                                                                     (Commit)t.PeeledTarget,
                                                                     v,
                                                                     runTest: false,
                                                                     forceRebuild: true );
                if( buildResult == null)
                {
                    return false;
                }
                // If the tag that triggered the build differs from the final "local/" one, removes it.
                Throw.DebugAssert( "Tags to rebuild are regular ones.", v.BuildMetaData.Length == 0 );
                if( t.CanonicalName != buildResult.VersionTag.CanonicalName )
                {
                    Repo.GitRepository.DeleteLocalTags( monitor, [t.CanonicalName] );
                }
                if( !v.IsLocal() )
                {
                    monitor.Warn( $"""
                        Tag '{v.ParsedText}' in '{Repo.DisplayPath}' has been rebuilt.
                        It must be manually published.
                        """ );
                }
            }
            return true;
        }
    }

    sealed class NoVersionTagIssue : World.Issue
    {
        readonly BuildPlugin _buildPlugin;
        readonly VersionTagInfo _versionTagInfo;
        readonly HotBranch _root;
        readonly string _vInit;

        public NoVersionTagIssue( BuildPlugin buildPlugin, VersionTagInfo versionTagInfo, string title, IRenderable body, HotBranch root, string vInit )
            : base( title, body, versionTagInfo.Repo )
        {
            _buildPlugin = buildPlugin;
            _versionTagInfo = versionTagInfo;
            _root = root;
            _vInit = vInit;
        }

        protected override ValueTask<bool> ExecuteAsync( IActivityMonitor monitor, CKliEnv context, World world )
        {
            Throw.DebugAssert( Repo != null && _root.GitBranch != null );
            using( monitor.OpenInfo( $"Fixing missing initial version in '{Repo.DisplayPath}' by creating '{_vInit}' on '{_root}'." ) )
            {
                Repo.GitRepository.Repository.Tags.Add( _vInit, _root.GitBranch.Tip );
            }
            return ValueTask.FromResult( true );
        }
    }
}
