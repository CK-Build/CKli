using CK.Core;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{

    public interface ILocalFeedProvider
    {
        /// <summary>
        /// Ensures that the LocalFeed physically available folder exists.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The LocalFeed directory info.</returns>
        IFileInfo EnsureLocalFeedFolder( IActivityMonitor m );

        /// <summary>
        /// Ensures that the LocalFeed/Blank physically available folder exists.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The LocalFeed/Blank directory info.</returns>
        IFileInfo EnsureLocalFeedBlankFolder( IActivityMonitor m );
    }
}
