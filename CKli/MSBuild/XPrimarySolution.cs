using CK.Env;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CK.Core;
using CK.Env.MSBuild;
using CK.Text;
using System.Linq;
using System.Xml.Linq;

namespace CKli
{
    /// <summary>
    /// A primary solution is the one at the root of the repository whose name
    /// must be the same as the working directory. It is available for the next siblings.
    /// </summary>
    public class XPrimarySolution : XSolutionBase
    {
        readonly XElement _xElement;

        public XPrimarySolution(
            Initializer initializer,
            XBranch branch,
            XSolutionSettings solutionSettings,
            XSolutionCentral central )
            : base( initializer,
                    branch,
                    central,
                    solutionSettings,
                    branch.Parent.Name + ".sln" )
        {
            if( !(initializer.Parent is XBranch) ) throw new Exception( "A primary solution must be a direct child of a Git branch." );
            _xElement = initializer.Element;
            initializer.Services.Add( this );
            branch.Parent.GitFolder.PluginManager.RegisterSettings( solutionSettings.SolutionSettings, branch.Name );
            foreach( var type in solutionSettings.SolutionSettings.Plugins )
            {
                branch.Parent.GitFolder.PluginManager.Register( type, branch.Name, allowGitPlugin: true );
            }
        }

        /// <summary>
        /// Gets the <see cref="XBranch"/> that is the direct parent.
        /// </summary>
        public new XBranch Parent => (XBranch)base.Parent;

        /// <summary>
        /// Gets whether <see cref="CK.Env.MSBuild.Solution.TestProjects"/> must be added to <see cref="CK.Env.MSBuild.Solution.PublishedProjects"/>.
        /// </summary>
        public bool TestProjectsArePublished { get; private set; }

        /// <summary>
        /// Gets a semicolon separated list of project names that must be removed from <see cref="CK.Env.MSBuild.Solution.PublishedProjects"/>.
        /// </summary>
        public string NotPublishedProjects { get; private set; }

        /// <summary>
        /// Overridden to load the primary solution with the current <see cref="XSolutionSettings.SolutionSettings"/>
        /// and applies <see cref="TestProjectsArePublished"/> and <see cref="NotPublishedProjects"/> attributes.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="reload">True to reload the solution.</param>
        /// <param name="projectToBranchName">Optional other branch for which the solution must be loaded.</param>
        /// <returns>The primary solution or null if not found.</returns>
        public override Solution GetSolution( IActivityMonitor m, bool reload, string projectToBranchName = null )
        {
            var (s, loaded) = SolutionCentral.MSBuildContext.FindOrLoadSolution(
                                                    m,
                                                    GetSolutionFilePath( projectToBranchName ),
                                                    null,
                                                    SolutionSpecialType.None,
                                                    reload );
            if( loaded )
            {
                if( TestProjectsArePublished )
                {
                    s.PublishedProjects.AddRange( s.TestProjects );
                }
                string[] unpublish = NotPublishedProjects?.Split( ';' );
                if( unpublish != null )
                {
                    foreach( var u in unpublish )
                    {
                        var idxU = s.PublishedProjects.IndexOf( p => p.Name == u );
                        if( idxU < 0 )
                        {
                            m.Warn( $"NotPublishedProject '{u}' not found in solution PublishedProjects: {s.PublishedProjects.Select( p => p.Name ).Concatenate()}." );
                        }
                        else s.PublishedProjects.RemoveAt( idxU );
                    }
                }
            }
            return s;
        }


    }
}
