using CK.Core;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Defines the Build project information.
    /// </summary>
    public class BuildProjectSpec
    {
        /// <summary>
        /// Initializes a new initial <see cref="BuildProjectSpec"/>.
        /// </summary>
        /// <param name="r">The element reader.</param>
        public BuildProjectSpec( in XElementReader r )
        {
            TargetFramework = r.HandleRequiredAttribute<string>( nameof( TargetFramework ) );
        }

        /// <summary>
        /// Gets the build project's TargetFramework ("netcorapp3.1", "net6.0", etc.).
        /// </summary>
        public string TargetFramework { get; }

    }
}
