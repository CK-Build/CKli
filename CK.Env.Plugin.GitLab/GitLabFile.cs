using CK.Env;
using CK.Env.Plugin;
using CK.Text;
using CK.Core;
using SharpYaml.Model;

namespace CK.Env.Plugin
{
    public class GitLabFile : YamlFilePluginBase, IGitBranchPlugin, ICommandMethodsProvider
    {
        public GitLabFile( GitFolder f, NormalizedPath branchPath )
            : base( f, branchPath, branchPath.AppendPart( ".gitlab-ci.yml" ) )
        {
        }

        public NormalizedPath CommandProviderName => FilePath;

        public bool CanApplySettings => Folder.CurrentBranchName == BranchPath.LastPart;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( !this.CheckCurrentBranch( m ) ) return;
            YamlMapping firstMapping = GetFirstMapping( m, true );
            if( firstMapping == null )
            {
                m.Error( "First mapping should not return null !" );
                return;
            }
            // We don't use GitLab for public repositories
            if( Folder.IsPublic || Folder.KnownGitProvider != KnownGitProvider.GitLab )
            {
                if( TextContent != null )
                {
                    m.Log( LogLevel.Info, "The project is public or the repository is not on GitLab, so we don't use GitLab and the .gitlab-ci.yml is not needed." );
                    Delete( m );
                }
                return;
            }
            // We use GitLab when the repository is private.
            YamlMapping codeCakeJob = FindOrCreateYamlElement( m, firstMapping, "codecakebuilder" );
            EnsureSequence( codeCakeJob, "tags", "windows" );
            EnsureSequence( codeCakeJob, "script", "dotnet run --project CodeCakeBuilder -nointeraction" );
            CreateOrUpdate( m, YamlMappingToString( m ) );
        }

    }
}