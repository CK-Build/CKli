using CK.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    /// <summary>
    /// Defines a NuGet source.
    /// </summary>
    public interface INuGetSource
    {
        /// <summary>
        /// Gets the feed name.
        /// Can not be null.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the feed url.
        /// Can not be null.
        /// </summary>
        string Url { get; }

        /// <summary>
        /// Gets optional credentials for the source.
        /// Can be null.
        /// </summary>
        SimpleCredentials Credentials { get; }

    }
}
