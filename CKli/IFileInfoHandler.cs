using CK.Core;
using CK.Text;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Text;

namespace CKli
{
    /// <summary>
    /// Exposes a minimal API that enables content for an item file to be transformed.
    /// </summary>
    public interface IFileInfoHandler
    {
        /// <summary>
        /// Gets the <see cref="XPathItem"/> file that is the target of the transformation.
        /// </summary>
        XPathItem TargetItem { get; }

        /// <summary>
        /// Gets the content path that must be transformed.
        /// </summary>
        NormalizedPath ContentPath { get; }

        /// <summary>
        /// Registers a transformation for the content of the <see cref="TargetItem"/>.
        /// </summary>
        /// <param name="processor">Transformation function.</param>
        void AddProcessor( Func<IActivityMonitor, IFileInfo, IFileInfo> processor );
    }
}
