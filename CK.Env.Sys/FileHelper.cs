using CK.Core;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace CK.Env
{
    public static class FileHelper
    {
        /// <summary>
        /// Helper that deletes a local directory (with linear timed retries).
        /// Throws the exception after 4 unsuccessful retries.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="dirPath">The directory path on the local file system to delete.</param>
        /// <returns>False if the directory did not exist, true if it has actually been deleted.</returns>
        public static bool RawDeleteLocalDirectory( IActivityMonitor m, string dirPath )
        {
            if( !Directory.Exists( dirPath ) ) return true;
            m.Info( $"Deleting {dirPath}." );
            int tryCount = 0;
            for(; ; )
            {
                try
                {
                    if( Directory.Exists( dirPath ) ) Directory.Delete( dirPath, true );
                    return true;
                }
                catch( Exception ex )
                {
                    m.Warn( $"Error while deleting {dirPath}.", ex );
                    if( ++tryCount > 4 ) throw;
                    System.Threading.Thread.Sleep( 100 * tryCount );
                }
            }
        }

        /// <summary>
        /// From https://github.com/libgit2/libgit2sharp/blob/f8e2d42ed9051fa5a5348c1a13d006f0cc069bc7/LibGit2Sharp.Tests/TestHelpers/DirectoryHelper.cs#L40
        /// </summary>
        /// <param name="directoryPath"></param>
        public static void DeleteDirectory( IActivityMonitor m, string directoryPath )
        {
            // From http://stackoverflow.com/questions/329355/cannot-delete-directory-with-directory-deletepath-true/329502#329502

            if( !Directory.Exists( directoryPath ) )
            {
                m.Trace( string.Format( "Directory '{0}' is missing and can't be removed.", directoryPath ) );
                return;
            }
            NormalizeAttributes( directoryPath );
            DeleteDirectory( m, directoryPath, maxAttempts: 5, initialTimeout: 16, timeoutFactor: 2 );
        }
        static readonly Type[] _whitelist = { typeof( IOException ), typeof( UnauthorizedAccessException ) };
        static void DeleteDirectory( IActivityMonitor m, string directoryPath, int maxAttempts, int initialTimeout, int timeoutFactor )
        {
            for( int attempt = 1; attempt <= maxAttempts; attempt++ )
            {
                try
                {
                    Directory.Delete( directoryPath, true );
                    return;
                }
                catch( Exception ex )
                {
                    var caughtExceptionType = ex.GetType();

                    if( !_whitelist.Any( knownExceptionType => knownExceptionType.GetTypeInfo().IsAssignableFrom( caughtExceptionType ) ) )
                    {
                        throw;
                    }

                    if( attempt < maxAttempts )
                    {
                        Thread.Sleep( initialTimeout * (int)Math.Pow( timeoutFactor, attempt - 1 ) );
                        continue;
                    }

                    m.Trace( string.Format( "{0}The directory '{1}' could not be deleted ({2} attempts were made) due to a {3}: {4}" +
                                                  "{0}Most of the time, this is due to an external process accessing the files in the temporary repositories created during the test runs, and keeping a handle on the directory, thus preventing the deletion of those files." +
                                                  "{0}Known and common causes include:" +
                                                  "{0}- Windows Search Indexer (go to the Indexing Options, in the Windows Control Panel, and exclude the bin folder of LibGit2Sharp.Tests)" +
                                                  "{0}- Antivirus (exclude the bin folder of LibGit2Sharp.Tests from the paths scanned by your real-time antivirus)" +
                                                  "{0}- TortoiseGit (change the 'Icon Overlays' settings, e.g., adding the bin folder of LibGit2Sharp.Tests to 'Exclude paths' and appending an '*' to exclude all subfolders as well)",
                        Environment.NewLine, Path.GetFullPath( directoryPath ), maxAttempts, caughtExceptionType, ex.Message ) );
                }
            }
        }

        static void NormalizeAttributes( string directoryPath )
        {
            string[] filePaths = Directory.GetFiles( directoryPath );
            string[] subdirectoryPaths = Directory.GetDirectories( directoryPath );

            foreach( string filePath in filePaths )
            {
                File.SetAttributes( filePath, FileAttributes.Normal );
            }
            foreach( string subdirectoryPath in subdirectoryPaths )
            {
                NormalizeAttributes( subdirectoryPath );
            }
            File.SetAttributes( directoryPath, FileAttributes.Normal );
        }
    }
}
