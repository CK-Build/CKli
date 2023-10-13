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
        /// Helper that deletes a local directory with retries.
        /// Throws the exception after 4 unsuccessful retries.
        /// From https://github.com/libgit2/libgit2sharp/blob/f8e2d42ed9051fa5a5348c1a13d006f0cc069bc7/LibGit2Sharp.Tests/TestHelpers/DirectoryHelper.cs#L40
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="directoryPath">The directory path on the local file system to delete.</param>
        /// <returns>True if the Directory was deleted or did not exist, false if it didn't deleted the directory.</returns>
        public static bool RawDeleteLocalDirectory( IActivityMonitor m, string directoryPath )
        {
            if( !Directory.Exists( directoryPath ) )
            {
                m.Trace( $"Directory '{directoryPath}' does not exist." );
                return true;
            }
            NormalizeAttributes( directoryPath );
            return DeleteDirectory( m, directoryPath, maxAttempts: 5, initialTimeout: 16, timeoutFactor: 2 );

            static bool DeleteDirectory( IActivityMonitor m, string directoryPath, int maxAttempts, int initialTimeout, int timeoutFactor )
            {
                for( int attempt = 1; attempt <= maxAttempts; attempt++ )
                {
                    try
                    {
                        if( Directory.Exists( directoryPath ) ) Directory.Delete( directoryPath, true );
                        return true;
                    }
                    catch( Exception ex ) when( ex is not IOException or UnauthorizedAccessException )
                    {
                        if( attempt < maxAttempts )
                        {
                            Thread.Sleep( initialTimeout * (int)Math.Pow( timeoutFactor, attempt - 1 ) );
                            continue;
                        }
                        m.Warn( $"Failed to delete directory '{Path.GetFullPath( directoryPath )}' ({maxAttempts} attempts were made) due to a {ex:C}: {ex.Message}" );
                    }
                }
                return false;
            }
        }

        public static void DirectoryCopy( string sourceDirName, string destDirName, bool copySubDirs )
        {
            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new DirectoryInfo( sourceDirName );

            if( !dir.Exists )
            {
                throw new DirectoryNotFoundException( $"Source directory does not exist or could not be found: {sourceDirName}." );
            }

            DirectoryInfo[] dirs = dir.GetDirectories();
            // If the destination directory doesn't exist, create it.
            if( !Directory.Exists( destDirName ) )
            {
                Directory.CreateDirectory( destDirName );
            }

            // Get the files in the directory and copy them to the new location.
            FileInfo[] files = dir.GetFiles();
            foreach( FileInfo file in files )
            {
                string temppath = Path.Combine( destDirName, file.Name );
                file.CopyTo( temppath, false );
            }

            // If copying subdirectories, copy them and their contents to new location.
            if( copySubDirs )
            {
                foreach( DirectoryInfo subdir in dirs )
                {
                    string temppath = Path.Combine( destDirName, subdir.Name );
                    DirectoryCopy( subdir.FullName, temppath, copySubDirs );
                }
            }
        }

        public static void NormalizeAttributes( string directoryPath )
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
