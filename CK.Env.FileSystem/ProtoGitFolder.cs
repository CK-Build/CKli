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
    public class ProtoGitFolder : GitRepositoryKey
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
            Uri url,
            bool isPublic,
            in NormalizedPath folderPath,
            IWorldName world,
            SecretKeyStore secretKeyStore,
            FileSystem fileSystem,
            CommandRegister commandRegister )
            : base(secretKeyStore, url, isPublic)
        {
            if( folderPath.IsEmptyPath ) throw new ArgumentException( "Empty path: FileSystem.Root path can not be a Git folder.", nameof( folderPath ) );
            if( folderPath.IsRooted ) throw new ArgumentException( "Must be relative to the FileSystem.Root.", nameof( folderPath ) );
            if( folderPath.EndsWith( ".git" ) ) throw new ArgumentException( "Path should be the repository directory and not the .git directory.", nameof( folderPath ) );

            if( world == null ) throw new ArgumentNullException( nameof( world ) );
            if( fileSystem == null ) throw new ArgumentNullException( nameof( fileSystem ) );
            if( commandRegister == null ) throw new ArgumentNullException( nameof( commandRegister ) );

            World = world;
            FolderPath = folderPath;
            FullPhysicalPath = fileSystem.Root.Combine( folderPath );
            FileSystem = fileSystem;
            CommandRegister = commandRegister;
            PluginRegistry = new GitPluginRegistry( folderPath );
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
        /// Gets the path that is relative to <see cref="FileSystem.Root"/> and contains the .git sub folder.
        /// </summary>
        public NormalizedPath FolderPath { get; }

        /// <summary>
        /// Gets the full path (that starts with the <see cref="FileSystem"/>' root path) of the Git folder.
        /// </summary>
        public NormalizedPath FullPhysicalPath { get; }

        /// <summary>
        /// Gets the command register.
        /// </summary>
        public CommandRegister CommandRegister { get; }

        /// <summary>
        /// Gets the plugin registry for this repository.
        /// </summary>
        public GitPluginRegistry PluginRegistry { get; }

        /// <summary>
        /// Checks out a working folder if needed or check that an existing one is
        /// bound to the <see cref="GitRepositoryKey.OriginUrl"/> 'origin' remote.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="git">The Git key.</param>
        /// <param name="workingFolder">The local working folder.</param>
        /// <param name="ensureHooks">True to create the standard hooks.</param>
        /// <param name="clonedBranchName">The initial branch name if cloning is done.</param>
        /// <returns>The LibGit2Sharp repository object or null on error.</returns>
        public static Repository EnsureWorkingFolder(
            IActivityMonitor m,
            GitRepositoryKey git,
            NormalizedPath workingFolder,
            bool ensureHooks,
            string clonedBranchName )
        {
            using( m.OpenTrace( $"Ensuring working folder '{workingFolder}' on '{git.OriginUrl}'." ) )
            {
                try
                {
                    var gitFolderPath = Path.Combine( workingFolder, ".git" );
                    if( !Directory.Exists( gitFolderPath ) )
                    {
                        using( m.OpenInfo( $"Checking out '{workingFolder}' from '{git.OriginUrl}' on {clonedBranchName}." ) )
                        {
                            Repository.Clone( git.OriginUrl.ToString(), workingFolder, new CloneOptions()
                            {
                                CredentialsProvider = ( url, user, cred ) => PATCredentialsHandler( m, git ),
                                BranchName = clonedBranchName,
                                Checkout = true
                            } );
                        }
                    }
                    else if( !Repository.IsValid( gitFolderPath ) )
                    {
                        throw new InvalidOperationException( $"Git folder {gitFolderPath} exists but is not a valid Repository." );
                    }
                    Repository r = new Repository( workingFolder );
                    var remote = r.Network.Remotes.FirstOrDefault( rem => rem.Url.Equals( git.OriginUrl.ToString(), StringComparison.OrdinalIgnoreCase ) );
                    if( remote == null || remote.Name != "origin" )
                    {

                        m.Fatal( $"Existing '{workingFolder}' must have its 'origin' remote set to '{git.OriginUrl}'. This must be fixed manually." );
                        r.Dispose();
                        return null;
                    }
                    if( ensureHooks ) EnsureHooks( m, workingFolder );
                    m.CloseGroup( "Repository is checked out." );
                    return r;
                }
                catch( Exception ex )
                {
                    m.Fatal( $"Failed to ensure '{workingFolder}'.", ex );
                    return null;
                }
            }
        }

        /// <summary>
        /// Ensures that the Git working folder actually exists and creates a
        /// GitFolder instance or returns null on error.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The GitFolder instance or null on error.</returns>
        public GitFolder CreateGitFolder( IActivityMonitor m )
        {
            var r = EnsureWorkingFolder( m, this, FullPhysicalPath, true, World.DevelopBranchName );
            if( r == null ) return null;
            return GitFolder.Create( m, r, this );
        }

        /// <summary>
        /// Credentials is read from the <see cref="GitRepositoryKey.SecretKeyStore"/>.
        /// This can not be implemented by GitRepositoryKey since LibGit2Sharp is not
        /// a dependency of CK.Env.Sys. 
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="git">The repository key.</param>
        /// <returns>The Credentials object that is null or a <see cref="UsernamePasswordCredentials"/>.</returns>
        static Credentials PATCredentialsHandler( IActivityMonitor m, GitRepositoryKey git )
        {
            if( git.KnownGitProvider == KnownGitProvider.Unknown ) throw new InvalidOperationException( "Unknown Git provider." );
            string pat = git.SecretKeyStore.GetSecretKey( m, git.ReadPATKeyName, !git.IsPublic );
            return pat != null
                    ? new UsernamePasswordCredentials() { Username = "CKli", Password = pat }
                    : null;
        }

        /// <summary>
        /// Binds this <see cref="GitRepositoryKey"/> to the static  <see cref="PATCredentialsHandler(IActivityMonitor, GitRepositoryKey)"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The Credentials object that is null or a <see cref="UsernamePasswordCredentials"/>.</returns>
        internal Credentials PATCredentialsHandler( IActivityMonitor m ) => PATCredentialsHandler( m, this );

        #region GitHooks

        static bool CheckHookDispatcherVersion( string scriptString )
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

        static void EnsureHooks( IActivityMonitor m, NormalizedPath root )
        {
            const string prepush = "pre_push";
            EnsureHookDispatcher( m, root, prepush );
            EnsureHookFile( m, root, prepush, "check_not_local_branch", _hook_check_not_local );
            EnsureHookFile( m, root, prepush, "check_no_commit_nopush", _hook_check_no_commit_nopush );
        }

        static NormalizedPath GetHooksDir( NormalizedPath root ) => root.AppendPart(".git").AppendPart("hooks");

        static NormalizedPath GetHookPath( NormalizedPath root, string hookName ) => GetHooksDir(root).AppendPart(hookName);

        static NormalizedPath GetMultiHooksDir( NormalizedPath root, string hookName ) => GetHooksDir(root).AppendPart(hookName + "_scripts");

        static void EnsureHookDispatcher( IActivityMonitor m, NormalizedPath root, string hookName )
        {
            NormalizedPath hookPath = GetHookPath( root, hookName );
            NormalizedPath multiHookDirectory = GetMultiHooksDir( root, hookName );
            bool hookPresent = File.Exists(hookPath);
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
            // Now hook file may not exist event if it did.
            if( !hookPresent )
            {
                File.WriteAllText( hookPath, HookPrePushScript( hookName ) );
                m.Info( $"Created dispatcher for {hookName} hooks" );
            }
        }

        static void EnsureHookFile( IActivityMonitor m, NormalizedPath root, string hookName, string scriptName, string script )
        {
            var hookPath = Path.Combine( GetMultiHooksDir( root, hookName ), scriptName );
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
        static string HookPrePushScript( string hookName ) =>
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
