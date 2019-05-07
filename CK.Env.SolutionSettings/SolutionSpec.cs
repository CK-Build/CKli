using CK.Core;
using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Defines a Solution specification.
    /// </summary>
    public class SolutionSpec : CommonSolutionSpec
    {
        public SolutionSpec( ICommonSolutionSpec other, ArtifactCenter artifacts, XElement applyConfig = null )
            : base( other, artifacts, applyConfig )
        {
            UseCKSetup = (bool?)applyConfig.Attribute( nameof( UseCKSetup ) ) ?? false;
            SqlServer = (string)applyConfig.Attribute( nameof( SqlServer ) );
            NPMProjects = applyConfig.Elements( nameof( NPMProjects ) )
                .ApplyAddRemoveClear( p => (string)p.AttributeRequired( "Folder" ), s => new NPMProjectSpec( p ) )
                .Values;

        }

        /// <summary>
        /// Gets whether the solution uses CKSetup components (defaults to false).
        /// When true (and when <see cref="NoDotNetUnitTests"/> is false), a RemoteStore.TestHelper.config file
        /// is created during build so that stores in CK-Env local folders are used instead of the default
        /// local (%UserProfile%AppData\Local\CKSetupStore) and default remote (https://cksetup.invenietis.net).
        /// </summary>
        bool UseCKSetup { get; }

        /// <summary>
        /// Gets the name of the SqlServer that is used.
        /// Defaults to null.
        /// Names are the ones of Appveyor (https://www.appveyor.com/docs/services-databases/).
        /// "2008R2SP2", "2012SP1", "2014", "2016", "2017".
        /// </summary>
        string SqlServer { get; }

        /// <summary>
        /// Gets the list of npm projects specifications.
        /// </summary>
        public IReadOnlyCollection<INPMProjectSpec> NPMProjects { get; }

    }
}
