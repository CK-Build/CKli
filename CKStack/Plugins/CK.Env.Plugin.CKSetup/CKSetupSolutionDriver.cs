using CK.Core;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

namespace CK.Env.Plugin
{

    public sealed partial class CKSetupSolutionDriver : GitBranchPluginBase, ICommandMethodsProvider
    {
        readonly SolutionDriver _driver;
        readonly RepositoryXmlFile _repositoryInfo;
        readonly SolutionSpec _solutionSpec;

        public CKSetupSolutionDriver( GitRepository f,
                                      NormalizedPath branchPath,
                                      SolutionDriver driver,
                                      RepositoryXmlFile repositoryInfo,
                                      SolutionSpec solutionSpec )
            : base( f, branchPath )
        {
            _driver = driver;
            _repositoryInfo = repositoryInfo;
            _solutionSpec = solutionSpec;
            _driver.RegisterSolutionProvider( new SolutionProvider( repositoryInfo ) );
        }

        /// <summary>
        /// Gets the whole solution driver.
        /// </summary>
        public SolutionDriver SolutionDriver => _driver;

        NormalizedPath ICommandMethodsProvider.CommandProviderName => BranchPath.AppendPart( "CKSetup" );

        [CommandMethod]
        public void ApplySettings( IActivityMonitor monitor )
        {
            if( !_solutionSpec.UseCKSetup && _solutionSpec.CKSetupComponentProjects.Count == 0 ) return;
            var solution = _driver.GetSolution( monitor, true );
            if( solution == null || solution.UseCKSetup() ) return;

            monitor.Info( $"Migrating from SolutionSpec to RepositoryInfo.xml." );
            var ckSetup = _repositoryInfo.EnsureDocument().Root!.EnsureElement( "CKSetup" );
            foreach( var name in _solutionSpec.CKSetupComponentProjects )
            {
                ckSetup.Add( new XElement( "Component", name ) );
            }
            _repositoryInfo.Save( monitor );
        }
    }
}
