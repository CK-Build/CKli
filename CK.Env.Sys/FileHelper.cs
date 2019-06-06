using CK.Core;
using System;
using System.IO;

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
    }
}
