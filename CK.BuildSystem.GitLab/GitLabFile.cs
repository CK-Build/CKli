using CK.Env;
using CK.Env.Plugins;
using CK.Text;
using CK.Core;
using SharpYaml.Model;

namespace CK.BuildSystem.GitLab
{
    public class GitLabFile : YamlFilePluginBase, IGitBranchPlugin, ICommandMethodsProvider
    {
        public GitLabFile( GitFolder f, NormalizedPath branchPath ) : base( f, branchPath, branchPath.AppendPart( ".gitlab-ci.yml" ) )
        {
        }

        public NormalizedPath CommandProviderName => FilePath;

        public bool CanApplySettings => Folder.CurrentBranchName == BranchPath.LastPart;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( !this.CheckCurrentBranch( m ) ) return;
            YamlMapping firstMapping = GetFirstMapping( m, true );
            if( firstMapping == null ) return;
            if( Folder.IsPublic )//We don't use GitLab for public repositories
            {
                m.Log(LogLevel.Info, "The project is public, so we don't use GitLab and the .gitlab-ci.yml is not needed." );
                Delete( m );
                return;
            }
            //We use GitLab when the repository is private.
            YamlMapping codeCakeJob = FindOrCreateYamlElement( m, firstMapping, "codecakebuilder" );
            EnsureSequence( codeCakeJob, "tags", "windows" );
            EnsureSequence( codeCakeJob, "script", "dotnet run --project CodeCakeBuilder -nointeraction" );
            CreateOrUpdate( m, YamlMappingToString( m ) );
        }
        
    }
}
