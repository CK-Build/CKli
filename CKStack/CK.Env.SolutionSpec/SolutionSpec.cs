using CK.Core;
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
            UseCKSetup = r.HandleOptionalAttribute( nameof( UseCKSetup ), false );
            SqlServer = r.HandleOptionalAttribute<string>( nameof( SqlServer ), null );
            TestProjectsArePublished = r.HandleOptionalAttribute( nameof( TestProjectsArePublished ), false );
            PublishProjectInDirectories = r.HandleOptionalAttribute( nameof( PublishProjectInDirectories ), false );
            NPMProjects = r.HandleCollection(
                nameof( NPMProjects ),
                new HashSet<NPMProjectSpec>(),
                eR => new NPMProjectSpec( eR ) );
            AngularWorkspaces = r.HandleCollection(
                nameof( AngularWorkspaces ),
                new HashSet<AngularWorkspaceSpec>(),
                eR => new AngularWorkspaceSpec( eR ) );
            CKSetupComponentProjects = r.HandleCollection(
                    nameof( CKSetupComponentProjects ),
                    new HashSet<string>(),
                    eR => eR.HandleRequiredAttribute<string>( "Name" )
                );
            UseCKSetup |= CKSetupComponentProjects.Count > 0;
            PublishedProjects = r.HandleCollection(
                 nameof( PublishedProjects ),
                    new HashSet<NormalizedPath>(),
                    eR => new NormalizedPath( eR.HandleRequiredAttribute<string>( "Folder" ) )
                );
            NotPublishedProjects = r.HandleCollection(
                    nameof( NotPublishedProjects ),
                    new HashSet<NormalizedPath>(),
                    eR => new NormalizedPath( eR.HandleRequiredAttribute<string>( "Folder" ) )
                );
        }

        /// <summary>
        /// Gets whether the solution uses CKSetup components (defaults to false).
        /// When true, a CKSetupStore.txt file is created during build at the root of the solution so that one of the
        /// store in /LocalFeed folders is used instead of the default local (%UserProfile%AppData\Local\CKSetupStore).
        /// Note that <see cref="CKSetupComponentProjects"/> (that describes CKSetup produces components) is
        /// independent of this configuration (using CKSetup components is independent of producing them).
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
        /// Gets the list of angular workspace specifications.
        /// </summary>
        public IReadOnlyCollection<IAngularWorkspaceSpec> AngularWorkspaces { get; }

        /// <summary>
        /// Gets the list of .Net project names that are CKSetup components.
        /// These names must be published .Net projects (see <see cref="PublishedProjects"/>
        /// and <see cref="NotPublishedProjects"/>): their names are necessarily the same as
        /// their NuGet packages.
        /// </summary>
        public IReadOnlyCollection<string> CKSetupComponentProjects { get; }

        /// <summary>
        /// Gets whether the projects not defined at the solution level should be published.
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
