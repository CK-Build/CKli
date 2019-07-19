using CK.Core;
using CK.Text;
using LibGit2Sharp;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CK.Env
{
    /// <summary>
    /// Captures all information required to instanciate an actual <see cref="GitFolder"/> in two steps.
    /// This split in two phases is mainly to first collect the secrets required by the
    /// repositories and resolve them before any actual instanciation.
    /// </summary>
    public class ProtoGitFolder
    {
        /// <summary>
        /// Initializes a new <see cref="ProtoGitFolder"/>.
        /// </summary>
        /// <param name="url">The url of the remote.</param>
        /// <param name="isPublic">Whether this repository is public.</param>
        /// <param name="folderPath">The path that is relative to <see cref="FileSystem.Root"/> and contains the .git sub folder.</param>
        /// <param name="world">The world name.</param>
        /// <param name="secretKeyStore">The secret key store.</param>
        /// <param name="fileSystem">=The file system.</param>
        /// <param name="commandRegister">The command register.</param>
        public ProtoGitFolder(
            string url,
            bool isPublic,
            in NormalizedPath folderPath,
            IWorldName world,
            SecretKeyStore secretKeyStore,
            FileSystem fileSystem,
            CommandRegister commandRegister )
        {
            if( url == null ) throw new ArgumentNullException( nameof( url ) );

            if( folderPath.IsEmptyPath ) throw new ArgumentException( "Empty path: FileSystem.Root path can not be a Git folder.", nameof( folderPath ) );
            if( folderPath.IsRooted ) throw new ArgumentException( "Must be relative to the FileSystem.Root.", nameof( folderPath ) );
            if( folderPath.EndsWith( ".git" ) ) throw new ArgumentException( "Path should be the repository directory and not the .git directory.", nameof( folderPath ) );

            if( world == null ) throw new ArgumentNullException( nameof( world ) );
            if( secretKeyStore == null ) throw new ArgumentNullException( nameof( secretKeyStore ) );
            if( fileSystem == null ) throw new ArgumentNullException( nameof( fileSystem ) );
            if( commandRegister == null ) throw new ArgumentNullException( nameof( commandRegister ) );

            if( url.IndexOf( "github.com", StringComparison.OrdinalIgnoreCase ) >= 0 ) KnownGitProvider = KnownGitProvider.GitHub;
            else if( url.IndexOf( "gitlab.com", StringComparison.OrdinalIgnoreCase ) >= 0 ) KnownGitProvider = KnownGitProvider.GitLab;
            else if( url.IndexOf( "dev.azure.com", StringComparison.OrdinalIgnoreCase ) >= 0 ) KnownGitProvider = KnownGitProvider.AzureDevOps;
            else if( url.IndexOf( "bitbucket.org", StringComparison.OrdinalIgnoreCase ) >= 0 ) KnownGitProvider = KnownGitProvider.Bitbucket;

            IsPublic = isPublic;
            OriginUrl = url;
            World = world;
            SecretKeyStore = secretKeyStore;
            FolderPath = folderPath;
            FullPhysicalPath = fileSystem.Root.Combine( folderPath );
            FileSystem = fileSystem;
            CommandRegister = commandRegister;
            PluginRegistry = new GitPluginRegistry( folderPath );

            if( KnownGitProvider != KnownGitProvider.Unknown )
            {
                ReadPATKeyName = GetPATName();
                var read = secretKeyStore.DeclareSecretKey( ReadPATKeyName, desc => desc ?? $"Used to read/clone solutions hosted by '{KnownGitProvider}':", isRequired: !IsPublic );
                WritePATKeyName = GetPATName( "_WRITE_PAT" );
                secretKeyStore.DeclareSecretKey( WritePATKeyName, desc => desc ?? $"Used to push solutions hosted by '{KnownGitProvider}'. This is required to publish builds.", subKey: read );
            }
        }

        /// <summary>
        /// Helper that formats the PAT name based on the kind of provider.
        /// </summary>
        /// <param name="suffix">Suffix to use.</param>
        /// <returns>The PAT name or null if <see cref="KnownGitProvider"/> is Unknown.</returns>
        public string GetPATName( string suffix = "_PAT" )
        {
            switch( KnownGitProvider )
            {
                case KnownGitProvider.Unknown: return null;
                case KnownGitProvider.AzureDevOps:
                    var regex = Regex.Match( OriginUrl, @"(?:\:\/\/)[^\/]*\/([^\/]*)" );
                    string organization = regex.Groups[1].Value;
                    return "AZURE_GIT_" + organization
                                .ToUpperInvariant()
                                .Replace( '-', '_' )
                                .Replace( ' ', '_' )
                                + suffix;
                default:
                    return KnownGitProvider.ToString().ToUpperInvariant() + "_GIT" + suffix;
            }
        }

        /// <summary>
        /// Gets the current <see cref="IWorldName"/>.
        /// </summary>
        public IWorldName World { get; }

        /// <summary>
        /// Gets the file system object.
        /// </summary>
        public FileSystem FileSystem { get; }

        /// <summary>
        /// Gets whether the Git repository is public.
        /// </summary>
        public bool IsPublic { get; }

        /// <summary>
        /// Gets the current remote origin url.
        /// </summary>
        public string OriginUrl { get; }

        /// <summary>
        /// Gets the known Git provider.
        /// </summary>
        public KnownGitProvider KnownGitProvider { get; }

        /// <summary>
        /// Gets the path that is relative to <see cref="FileSystem.Root"/> and contains the .git sub folder.
        /// </summary>
        public NormalizedPath FolderPath { get; }

        /// <summary>
        /// Gets the full path (that starts with the <see cref="FileSystem"/>' root path) of the Git folder.
        /// </summary>
        public NormalizedPath FullPhysicalPath { get; }

        /// <summary>
        /// Gets the secret key store.
        /// </summary>
        public SecretKeyStore SecretKeyStore { get; }

        /// <summary>
        /// Gets the basic, read/clone, PAT key name for this repository.
        /// Note that if <see cref="IsPublic"/> is true, this PAT should be useless: anyone should be able to
        /// read/clone the repository.
        /// </summary>
        public string ReadPATKeyName { get; }

        /// <summary>
        /// Gets the write PAT key name for this repository.
        /// This PAT must allow pushes to the repository.
        /// </summary>
        public string WritePATKeyName { get; }

        /// <summary>
        /// Gets the command register.
        /// </summary>
        public CommandRegister CommandRegister { get; }

        /// <summary>
        /// Gets the plugin registry for this repository.
        /// </summary>
        public GitPluginRegistry PluginRegistry { get; }

        /// <summary>
        /// Ensures that the Git working folder actually exists and creates a
        /// GitFolder instance.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The GitFolder instance.</returns>
        public GitFolder CreateGitFolder( IActivityMonitor m )
        {
            using( m.OpenTrace( $"Ensuring git repository {FullPhysicalPath}" ) )
            {

                var gitFolderPath = Path.Combine( FolderPath, ".git" );
                if( !Directory.Exists( gitFolderPath ) )
                {
                    using( m.OpenInfo( $"Checking out '{FolderPath}' from '{OriginUrl}' on {World.DevelopBranchName}." ) )
                    {
                        Repository.Clone( OriginUrl, FolderPath, new CloneOptions()
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
                return GitFolder.EnsureGitFolderCorrectSetup( m, this );

            }
        }

        internal Credentials PATCredentialsHandler( IActivityMonitor m, string url )
        {
            if( KnownGitProvider == KnownGitProvider.Unknown ) throw new InvalidOperationException( "Unknown Git provider." );
            string pat = SecretKeyStore.GetSecretKey( m, ReadPATKeyName, true );
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

        string GetHooksDir => Path.Combine( FolderPath, ".git", "hooks" );

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
