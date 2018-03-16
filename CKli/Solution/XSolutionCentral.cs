using CK.Core;
using CK.Env;
using CK.Env.MSBuild;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CKli
{
    public class XSolutionCentral : XTypedObject
    {
        readonly ProjectFileContext _projectContext;
        readonly Dictionary<NormalizedPath, SolutionFile> _solutions;

        public XSolutionCentral(
            FileSystem fileSystem,
            Initializer initializer )
            : base( initializer )
        {
            _solutions = new Dictionary<NormalizedPath, SolutionFile>();
            _projectContext = new ProjectFileContext( fileSystem );
            initializer.Services.Add( this );
        }

        /// <summary>
        /// Gets all the solution defined among the next siblings.
        /// </summary>
        public IEnumerable<XSolutionBase> AllSolutions => NextSiblings.SelectMany( s => s.Descendants<XSolutionBase>() );

        /// <summary>
        /// Gets the primary solutions defined among the next siblings.
        /// </summary>
        public IEnumerable<XPrimarySolution> PrimarySolutions => NextSiblings.SelectMany( s => s.Descendants<XPrimarySolution>() );

        /// <summary>
        /// Gets the primary solutions defined among the next siblings.
        /// </summary>
        public IEnumerable<XSecondarySolution> SecondarySolutions => NextSiblings.SelectMany( s => s.Descendants<XSecondarySolution>() );

        /// <summary>
        /// Loads or reloads a given solution.
        /// Returns null if an error occurred.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="filePath">Path to the .sln file.</param>
        /// <param name="forceReload">True to force the reload of the solution.</param>
        /// <returns>The solution or null on error.</returns>
        public SolutionFile GetSolution( IActivityMonitor m, NormalizedPath filePath, bool forceReload = false )
        {
            if( !_solutions.TryGetValue( filePath, out var solution ) || forceReload )
            {
                XSolutionBase xS = AllSolutions.FirstOrDefault( s => s.FullPath == filePath );
                if( xS == null )
                {
                    m.Error( $"Unable to find a XPrimarySolution or XSecondarySolution for '{filePath}'." );
                    return null;
                }
                solution = xS.ReadSolutionFile( m, forceReload );
            }
            return solution;
        }

        /// <summary>
        /// This is to be called only by XSolutionBase.
        /// Returns null if an error occurred.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="filePath">Path to the .sln file.</param>
        /// <param name="primarySolution">The primary solution if this is a secondary solution.</param>
        /// <param name="forceReload">True to force the reload of the solution.</param>
        /// <returns>The solution or null on error.</returns>
        internal SolutionFile FindOrCreateSolutionFile( IActivityMonitor m, NormalizedPath filePath, SolutionFile primarySolution = null, bool forceReload = false )
        {
            if( !_solutions.TryGetValue( filePath, out var solution ) || forceReload )
            {
                solution = SolutionFile.Create( m, _projectContext, primarySolution, filePath );
                if( solution == null ) return null;
                _solutions[filePath] = solution;
            }
            return solution;
        }

        /// <summary>
        /// Gets all <see cref="SolutionFile"/> from all next <see cref="XPrimarySolution"/> siblings.
        /// Returns null if an error occurred.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="forceReload">True to force the reload of all the primary solutions.</param>
        /// <returns>All the primary solutions or null on error.</returns>
        public IEnumerable<SolutionFile> LoadAllPrimarySolutions( IActivityMonitor m, bool forceReload )
        {
            return Load<XPrimarySolution>( m, forceReload );
        }

        /// <summary>
        /// Gets all <see cref="SolutionFile"/> from all next <see cref="XPrimarySolution"/> and <see cref="XSecondarySolution"/> siblings.
        /// Returns null if an error occurred.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="forceReload">True to force the reload of all the solutions.</param>
        /// <returns>All the solutions or null on error.</returns>
        public IEnumerable<SolutionFile> LoadAllSolutions( IActivityMonitor m, bool forceReload )
        {
            return Load<XSolutionBase>( m, forceReload );
        }

        IEnumerable<SolutionFile> Load<T>( IActivityMonitor m, bool forceReload ) where T : XSolutionBase
        {
            var solutions = NextSiblings
                                .SelectMany( s => s.Descendants<T>() )
                                .Select( s => s.ReadSolutionFile( m, forceReload ) );
            return solutions.All( f => f != null ) ? solutions : null;
        }

    }
}
