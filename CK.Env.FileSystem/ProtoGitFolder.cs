using CK.Core;
using CK.Text;
using LibGit2Sharp;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace CK.Env
{
    public class ProtoGitFolder
    {
        public ProtoGitFolder(
            string url,
            string fullPhysicalPath,
            IWorldName world,
            ISecretKeyStore secretKeyStore,
            FileSystem fileSystem,
            CommandRegister commandRegister )
        {
            if( url == null ) throw new ArgumentNullException( nameof( url ) );
            if( fullPhysicalPath == null ) throw new ArgumentNullException( nameof( fullPhysicalPath ) );
            if( fullPhysicalPath.Contains( ".git" ) ) throw new ArgumentException( "Path should be the repository directory and not the .git directory" );
            if( world == null ) throw new ArgumentNullException( nameof( world ) );
            if( secretKeyStore == null ) throw new ArgumentNullException( nameof( secretKeyStore ) );
            if( fileSystem == null ) throw new ArgumentNullException( nameof( fileSystem ) );
            if( commandRegister == null ) throw new ArgumentNullException( nameof( commandRegister ) );

            if( url.IndexOf( "github.com", StringComparison.OrdinalIgnoreCase ) >= 0 ) KnownGitProvider = KnownGitProvider.GitHub;
            else if( url.IndexOf( "gitlab.com", StringComparison.OrdinalIgnoreCase ) >= 0 ) KnownGitProvider = KnownGitProvider.GitLab;
            else if( url.IndexOf( "dev.azure.com", StringComparison.OrdinalIgnoreCase ) >= 0 ) KnownGitProvider = KnownGitProvider.AzureDevOps;
            else if( url.IndexOf( "bitbucket.org", StringComparison.OrdinalIgnoreCase ) >= 0 ) KnownGitProvider = KnownGitProvider.Bitbucket;

            OriginUrl = url;
            World = world;
            SecretKeyStore = secretKeyStore;
            FullPhysicalPath = fullPhysicalPath;
            FileSystem = fileSystem;
            CommandRegister = commandRegister;
        }

        /// <summary>
        /// Gets the current <see cref="IWorldName"/>.
        /// </summary>
        public IWorldName World { get; }

        public FileSystem FileSystem { get; }

        /// <summary>
        /// Gets the current remote origin url.
        /// </summary>
        public string OriginUrl { get; }

        /// <summary>
        /// Gets the known Git provider.
        /// </summary>
        public KnownGitProvider KnownGitProvider { get; }

        /// <summary>
        /// Gets the full path (that starts with the <see cref="FileSystem"/>' root path) of the Git folder.
        /// </summary>
        public NormalizedPath FullPhysicalPath { get; }

        public ISecretKeyStore SecretKeyStore { get; }

        public CommandRegister CommandRegister { get; }

        /// <summary>
        /// Ensures that the Git working folder actually exists and creates a
        /// GitFolder instance.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="isPublic">Whether <see cref="GitFolder.IsPublic"/> is true.</param>
        /// <returns>The GitFolder instance.</returns>
        public GitFolder CreateGitFolder( IActivityMonitor m, bool isPublic )
        {
            using( m.OpenTrace( $"Ensuring git repository {FullPhysicalPath}" ) )
            {

                var gitFolderPath = Path.Combine( FullPhysicalPath, ".git" );
                if( !Directory.Exists( gitFolderPath ) )
                {
                    using( m.OpenInfo( $"Checking out '{FullPhysicalPath}' from '{OriginUrl}' on {World.DevelopBranchName}." ) )
                    {
                        Repository.Clone( OriginUrl, FullPhysicalPath, new CloneOptions()
                        {
                            CredentialsProvider = ( url, user, cred ) => PATCredentialsHandler( m, url ),
                            BranchName = World.DevelopBranchName,
                            Checkout = true
                        } );
                    }
                }
                else if( !Repository.IsValid( gitFolderPath ) )
                {
                    throw new InvalidOperationException( $"Git folder {gitFolderPath} exists but is not a valid Repository" );
                }
                else
                {
                    m.Trace( "Repository is checked out." );
                }

                EnsureHooks( m );
            }
            return new GitFolder( this, isPublic );
        }

        public Credentials PATCredentialsHandler( IActivityMonitor m, string url )
        {
            string keyName;
            switch( KnownGitProvider )
            {
                case KnownGitProvider.AzureDevOps:
                    var regex = Regex.Match( url, @"(?:\:\/\/)[^\/]*\/([^\/]*)" );
                    string organization = regex.Groups[1].Value;
                    keyName = "AZURE_GIT_" + organization
                        .ToUpperInvariant()
                        .Replace( '-', '_' )
                        .Replace( ' ', '_' )
                        + "_PAT";
                    break;
                default:
                    keyName = KnownGitProvider.ToString() + "_GIT_PAT";
                    break;
            }
            string pat = SecretKeyStore.GetSecretKey( m, keyName, true );
            return new UsernamePasswordCredentials()
            {
                Username = "CK-Env",
                Password = pat
            };
        }


        #region GitHooks


        bool CheckHookDispatcherVersion( string scriptString )
        {
            string[] lines = scriptString.Split( '\n' );
            if( lines.Length <= 2 ) return false;
            if( lines[1].Contains( "#script-dispatcher-0.0.0" ) ) return true;
            if( lines[1].Contains( "script-dispatcher" ) )
            {
                throw new NotImplementedException( "Upgrade version of hook dispatcher is not implemented yet." );
            }
            return false;
        }

        void EnsureHooks( IActivityMonitor m )
        {
            const string prepush = "pre_push";
            EnsureHookDispatcher( m, "pre_push" );
            EnsureHookFile( m, prepush, "check_not_local_branch", _hook_check_not_local );
            EnsureHookFile( m, prepush, "check_no_commit_nopush", _hook_check_no_commit_nopush );
        }

        string GetHooksDir => Path.Combine( FullPhysicalPath, ".git", "hooks" );

        string GetHookPath( string hookName ) => Path.Combine( GetHooksDir, hookName );

        string GetMultiHooksDir( string hookName ) => Path.Combine( GetHooksDir, hookName + "_scripts/" );

        void EnsureHookDispatcher( IActivityMonitor m, string hookName )
        {
            string hookPath = GetHookPath( hookName );
            string multiHookDirectory = GetMultiHooksDir( hookName );
            bool hookPresent = File.Exists( hookPath );
            Directory.CreateDirectory( multiHookDirectory );
            if( hookPresent )
            {
                string currentScript = File.ReadAllText( hookPath );
                if( currentScript == _hook_check_not_local )
                {
                    m.Info( "Detected our old prepush script. It's not an userscript, we can remove it." );
                    File.Delete( hookPath );
                    hookPresent = false;
                }
                else
                {
                    //replacing the hook dispatcher on a new version is not implemented yet, we just save the script to not destroy user hooks.
                    if( !CheckHookDispatcherVersion( currentScript ) )
                    {
                        File.Move( hookPath, Path.Combine( multiHookDirectory, hookName ) );
                        m.Info( $"Git hook {hookName} was not the dispatcher. Moved user hook to {multiHookDirectory}." );
                        hookPresent = false;
                    }
                    else
                    {
                        m.Trace( $"The current {hookName} hook is our dispatcher." );
                    }
                }
            }
            //Now hook file may not exist event if it did.
            if( !hookPresent )
            {
                File.WriteAllText( hookPath, HookPrePushScript( hookName ) );
                m.Info( $"Created dispatcher for {hookName} hooks" );
            }
        }

        void EnsureHookFile( IActivityMonitor m, string hookName, string scriptName, string script )
        {
            var hookPath = Path.Combine( GetMultiHooksDir( hookName ), scriptName );
            bool currentHookIsUpToDate = File.Exists( hookPath ) && File.ReadAllText( hookPath ) == script;
            if( !currentHookIsUpToDate )
            {
                m.Info( "git " + Path.GetFileName( hookPath ) + " hook not up to date. Updating!" );
                File.WriteAllText( hookPath, script );
            }
            else
            {
                m.Trace( $"The script {hookName}_scripts/{scriptName} is up to date." );
            }
        }

        const string _hook_check_not_local =
@"#!/bin/sh
# Abort push on -local branches

while read local_ref local_sha remote_ref remote_sha 
do 
	if [[ $local_ref == *-local ]]
	then
		echo >&2 ""Pushing on a -local branch, aborting!""
        exit 1
	fi
done
exit 0
";
        const string _hook_check_no_commit_nopush =
@"#!/bin/sh
# inspired from https://github.com/bobgilmore/githooks/blob/master/pre-push
# Hook stopping the push if we find a [NOPUSH] commit.
remote=""$1""
url=""$2""

z40=0000000000000000000000000000000000000000

echo ""Checking if a commit contain NOPUSH...""
while read local_ref local_sha remote_ref remote_sha
do
	if [ ""$local_sha"" = $z40 ]
	then
		echo ""Deleting files, OK.""
	else
		if [ ""$remote_sha"" = $z40 ]
		then
			# New branch, examine all commits
			range=""$local_sha""
		else
			# Update to existing branch, examine new commits
			range=""$remote_sha..$local_sha""
		fi

		# Check for foo commit
		commit=`git rev-list -n 1 --grep 'NOPUSH' ""$range""`
    echo $commit
		if [ -n ""$commit"" ]
		then
			echo >&2 ""ERROR: Found commit message containing 'NOPUSH' in $local_ref so you should not push this commit !!!""
      echo >&2 ""Commit containing the message: $commit""
			exit 1
		fi
	fi
done
echo ""No commit found with NOPUSH. Push can continue.""
exit 1
";
        string HookPrePushScript( string hookName ) =>
$@"#!/bin/sh
#script-dispatcher-0.0.0
# Hook that execute all scripts in a directory

remote=""$1"";
url=""$2"";
hook_directory="".git/hooks""
search_dir=""{hookName}_scripts""

search_path=""$hook_directory/$search_dir""
i=0
for scriptFile in ""$search_path""/*; do
  i=$((i+=1))
  echo ""Running script $scriptFile"";
  exitCode=exec ""$scriptFile"" ""$@"" || break;  # execute successfully or break
  # Or more explicitly: if this execution fails, then stop the `for`:
   if ! bash ""$scriptFile""; then
   >&2 echo ""Script $scriptFile failed. Aborting push."";
   exit $exitCode;
   break;
   fi
done
echo ""Executed successfully $i scripts.""
exit 1
";

        #endregion GitHooks
    }
}
