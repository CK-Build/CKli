using CK.Core;
using CK.Text;
using SharpYaml.Model;

namespace CK.Env.Plugin
{
    public class GitLabFile : YamlFilePluginBase, IGitBranchPlugin, ICommandMethodsProvider
    {
        public GitLabFile( GitRepository f, NormalizedPath branchPath )
            : base( f, branchPath, branchPath.AppendPart( ".gitlab-ci.yml" ) )
        {
        }

        public NormalizedPath CommandProviderName => FilePath;

        public bool CanApplySettings => GitFolder.CurrentBranchName == BranchPath.LastPart;

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
            if( GitFolder.IsPublic || GitFolder.KnownGitProvider != KnownGitProvider.GitLab )
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
            SetSequence( codeCakeJob, "tags", new YamlValue( "windows" ) );
            SetSequence( codeCakeJob, "script",
                new YamlValue( "dotnet run --project CodeCakeBuilder -nointeraction" )
            );
            codeCakeJob["artifacts"] =
                new YamlMapping()
                {
                    ["paths"] = new YamlSequence()
                    {
                        new YamlValue(@"'**\Tests\**\TestResults\*.trx'"),
                        new YamlValue(@"'**Tests\**\Logs\**\*'"),
                    },
                    ["when"] = new YamlValue( "always" ),
                    ["expire_in"] = new YamlValue( "6 month" )
                };
            CreateOrUpdate( m, YamlMappingToString( m ) );
        }

    }
}
