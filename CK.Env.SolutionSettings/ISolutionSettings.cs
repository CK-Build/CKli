using System;
using System.Collections.Generic;

namespace CK.Env
{
    public interface ISolutionSettings
    {
        /// <summary>
        /// Gets whether the solution has no unit tests.
        /// </summary>
        bool NoUnitTests { get; }

        /// <summary>
        /// Gets whether no strong name singing should be used.
        /// </summary>
        bool NoStrongNameSigning { get; }

        /// <summary>
        /// Gets whether the solution uses CKSetup components.
        /// When true (and when <see cref="NoUnitTests"/> is false), a RemoteStore.TestHelper.config file
        /// is created during build so that stores in CK-Env local folders are used instead of the default
        /// local (%UserProfile%AppData\Local\CKSetupStore) and default remote (https://cksetup.invenietis.net).
        /// </summary>
        bool UseCKSetup { get; }

       /// <summary>
        /// Gets whether source link is disabled.
        /// Impacts Common/Shared.props file.
        /// </summary>
        bool DisableSourceLink { get; }

        /// <summary>
        /// Gets the name of the SqlServer that is used.
        /// Defaults to null.
        /// Names are the ones of Appveyor (https://www.appveyor.com/docs/services-databases/).
        /// "2008R2SP2", "2012SP1", "2014", "2016", "2017".
        /// </summary>
        string SqlServer { get; }

        /// <summary>
        /// Defines the set of NuGet sources that is used.
        /// Impacts NuGet.config file.
        /// </summary>
        IReadOnlyCollection<INuGetSource> NuGetSources { get; }

        /// <summary>
        /// Gets the NuGet source names that must be excluded.
        /// Must be used to clean up existing source names that must no more be used.
        /// Impacts NuGet.config file.
        /// </summary>
        IReadOnlyCollection<string> RemoveNuGetSourceNames { get; }


        /// <summary>
        /// Gets the repository where produced artifacts must be pushed.
        /// </summary>
        IReadOnlyCollection<IArtifactRepository> ArtifactTargets { get; }

        /// <summary>
        /// Defines the set of plugins that must apply.
        /// </summary>
        IReadOnlyCollection<Type> Plugins { get; }

    }
}
