using System;
using System.Linq;
using CK.Core;
using CK.Env;
using CK.Text;
using CSemVer;

namespace CKli
{
    public class ReleaseVersionSelector : IReleaseVersionSelector
    {
        /// <summary>
        /// This method must choose between possible versions. It may return null to cancel the process.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="c">The context.</param>
        public void ChooseFinalVersion( IActivityMonitor m, IReleaseVersionSelectorContext c )
        {
            Console.WriteLine( "=========" );
            Console.Write( $"======== {c.Solution.UniqueSolutionName} " );
            if( c.PreviousVersionCommitSha != null )
            {
                Console.Write( $" last release: {c.PreviousVersion}" );
                var diffs = c.Solution.GitRepository.GetReleaseDiff( m, c.PreviousVersionCommitSha, c.Solution.GeneratedPackages );
                if( diffs == null )
                {
                    c.Cancel();
                    return;
                }
                if( diffs.All( d => d.DiffType == PackageReleaseDiffType.None ) )
                {
                    Console.WriteLine( $" (No change in {c.Solution.GeneratedPackages.Select( p => p.Name ).Concatenate()})" );
                }
                else
                {
                    Console.WriteLine( ", changes:" );
                }
                foreach( var d in diffs )
                {
                    d.DumpDiff();
                }
            }
            else
            {
                Console.WriteLine( "(No previous released version)" );
            }

            foreach( var kv in c.PossibleVersions )
            {
                Console.WriteLine( $"= {(int)kv.Key} - {kv.Key} => {kv.Value.Select( v => v.NormalizedText ).Concatenate()}" );
            }
            if( c.CanUsePreviouslyResolvedInfo )
            {
                Console.WriteLine( $"= A - Already selected Level and Version: {c.PreviouslyResolvedInfo.Level} - {c.PreviouslyResolvedInfo.Version}" );
            }
            Console.WriteLine( "= X - Cancel." );
            ReleaseLevel level = ReleaseLevel.None;
            char a;
            do
            {
                while( "0123AX".IndexOf( (a = Console.ReadKey().KeyChar) ) < 0 ) Console.Write( '\b' );
                if( a == 'X' ) c.Cancel();
                if( a == 'A' )
                {
                    c.SetChoice( c.PreviouslyResolvedInfo.Level, c.PreviouslyResolvedInfo.Version );
                }
                else
                {
                    level = (ReleaseLevel)(a - '0');
                }
            }
            while( !c.IsAnswered && c.PossibleVersions[level].Count == 0 );
            if( !c.IsAnswered )
            {
                Console.WriteLine( $"= Selected: {level}, now choose a version:" );
                var possibleVersions = c.PossibleVersions[level];
                for( int i = 0; i < possibleVersions.Count; ++i )
                {
                    Console.WriteLine( $"= {i} - {possibleVersions[i]}" );
                }
                Console.WriteLine( $"= X - Cancel." );
                while( !c.IsAnswered )
                {
                    Console.Write( $"= (Enter the final release number and press enter)> " );
                    string line = Console.ReadLine();
                    if( line == "X" )
                    {
                        c.Cancel();
                    }
                    else if( Int32.TryParse( line, out var num ) && num >= 0 && num < possibleVersions.Count )
                    {
                        c.SetChoice( level, possibleVersions[num] );
                    }
                    Console.WriteLine();
                }
            }
        }

        /// <summary>
        /// Called whenever the final version is already set by a version tag on the commit point.
        /// We consider this version to be already released.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="solution">The solution.</param>
        /// <param name="version">The tagged version.</param>
        /// <param name="isContentTag">False if it the tag is on the commit point, true if the tag is a content tag.</param>
        /// <returns>True to continue, false to cancel the current session.</returns>
        public bool OnAlreadyReleased( IActivityMonitor m, IDependentSolution solution, CSVersion version, bool isContentTag )
        {
            Console.WriteLine( "=========" );
            Console.WriteLine( $"========  {solution.UniqueSolutionName} is already released in version '{version}'." );
            return true;
        }

    }
}
