using CK.Core;
using CK.Text;
using System.Collections.Generic;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Defines a Solution specification.
    /// </summary>
    public class SolutionSpec : SharedSolutionSpec
    {
        public SolutionSpec( SharedSolutionSpec shared, in XElementReader r )
            : base( shared, r )
        {
            var e = r.Element;

            UseCKSetup = r.HandleOptionalAttribute( nameof( UseCKSetup ), false );
            SqlServer = r.HandleOptionalAttribute<string>( nameof( SqlServer ), null );
            TestProjectsArePublished = r.HandleOptionalAttribute( nameof( TestProjectsArePublished ), false );
            PublishProjectInDirectories = r.HandleOptionalAttribute( nameof( PublishProjectInDirectories ), false );
            NPMProjects = e.Elements( nameof( NPMProjects ) )
                .ApplyAddRemoveClear( p => (string)p.AttributeRequired( "Folder" ), p => new NPMProjectSpec( p ) )
                .Values;

            CKSetupComponentProjects = e.Elements( nameof( CKSetupComponentProjects ) )
                            .ApplyAddRemoveClear( s => (string)s.AttributeRequired( "Name" ) );

            PublishedProjects = e.Elements( nameof( PublishedProjects ) )
                            .ApplyAddRemoveClear( s => new NormalizedPath( (string)s.AttributeRequired( "Folder" ) ) );


            NotPublishedProjects = e.Elements( nameof( NotPublishedProjects ) )
                            .ApplyAddRemoveClear( s => new NormalizedPath( (string)s.AttributeRequired( "Folder" ) ) );
        }

        /// <summary>
        /// Gets whether the solution uses CKSetup components (defaults to false).
        /// When true (and when <see cref="NoDotNetUnitTests"/> is false), a RemoteStore.TestHelper.config file
        /// is created during build so that stores in CK-Env local folders are used instead of the default
        /// local (%UserProfile%AppData\Local\CKSetupStore) and default remote (https://cksetup.invenietis.net).
        /// </summary>
        public bool UseCKSetup { get; }

        /// <summary>
        /// Gets the name of the SqlServer that is used.
        /// Defaults to null.
        /// Names are the ones of Appveyor (https://www.appveyor.com/docs/services-databases/).
        /// "2008R2SP2", "2012SP1", "2014", "2016", "2017".
        /// </summary>
        public string SqlServer { get; }

        /// <summary>
        /// Gets whether .Net Test projects (all projects with a name that ends with ".Tests") of the actual
        /// solution must be published as NuGet packages.
        /// Defaults to false.
        /// </summary>
        public bool TestProjectsArePublished { get; }

        /// <summary>
        /// Gets the list of npm projects specifications.
        /// </summary>
        public IReadOnlyCollection<INPMProjectSpec> NPMProjects { get; }

        /// <summary>
        /// Gets the list of .Net project names that are CKSetup components.
        /// These names must be published .Net projects (see <see cref="PublishedProjects"/>
        /// and <see cref="NotPublishedProjects"/>): their names are necessarily the same as
        /// their NuGet packages.
        /// </summary>
        public IReadOnlyCollection<string> CKSetupComponentProjects { get; }

        /// <summary>
        /// Gets wether the projects not defined at the solution level should be published.
        /// This does not impact the projects in the Tests folder.
        /// Defaults to false.
        /// </summary>
        public bool PublishProjectInDirectories { get; }

        /// <summary>
        /// Gets the white list of explicitly published .Net projects paths.
        /// When empty (or not specified), the published .Net projects are the ones
        /// defined at the solution level whose name does not end with ".Test" (except
        /// if <see cref="TestProjectsArePublished"/>) and that is not the build project (CodeCakeBuilder).
        /// </summary>
        public IReadOnlyCollection<NormalizedPath> PublishedProjects { get; }


        /// <summary>
        /// Gets the optional set of .Net project folders that must not be published.
        /// </summary>
        public IReadOnlyCollection<NormalizedPath> NotPublishedProjects { get; }

    }
}
