using CK.Core;
using CK.Build;
using CK.Env;
using CK.Env.DependencyModel;
using CK.Text;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Linq;

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
            Console.Write( $"======== {c.Solution.Solution.Name} " );
            if( c.PreviousVersion != null )
            {
                Console.Write( $" last release: {c.PreviousVersion.ThisTag}" );
                var diffResult = c.GetProjectsDiff( m );
                if( diffResult == null )
                {
                    c.Cancel();
                    return;
                }

                if( diffResult.Diffs.All( d => d.DiffType == DiffRootResultType.None ) && diffResult.Others.DiffType == DiffRootResultType.None )
                {
                    Console.WriteLine( $" (No change in {c.Solution.Solution.GeneratedArtifacts.Select( p => p.Artifact.Name ).Concatenate()})" );
                }
                else
                {
                    Console.WriteLine( ", changes:" );
                }

                Console.WriteLine( diffResult.ToString() );
            }
            else
            {
                Console.WriteLine( "(No previous released version)" );
            }

            var projExRefNotRelease = c.Solution.Solution.Projects
                .Where( p => p.IsPublished && p.PackageReferences.Any( q => q.Kind == ArtifactDependencyKind.Transitive ) )
                .Select( p =>
                {
                    List<ProjectPackageReference> pcks = p.PackageReferences
                    .Where( q => q.Kind == ArtifactDependencyKind.Transitive )
                    .Where(
                        q => !c.Solution.ImportedLocalPackages
                                .Any( s => s.Package.Artifact == q.Target.Artifact )
                         )
                    .ToList();
                    if( pcks.Any() )
                    {
                        PackageQuality worstQuality = pcks.Select( q => q.Target.Version.PackageQuality ).Min();
                        return (p, pcks.Where( q => q.Target.Version.PackageQuality == worstQuality ));
                    }
                    p = null;
                    return (p, Array.Empty<ProjectPackageReference>());
                } )//There should be at least one package reference
                .Where( x => x.p != null )
                .GroupBy( p => p.Item2.First().Target.Version.PackageQuality ).ToList(); //ugliest LINQ i ever wrote, should take 3 lines.
            var min = projExRefNotRelease.Any() ? projExRefNotRelease.Min( q => q.Key ) : PackageQuality.None;
            var worst = min != PackageQuality.None
                            ? projExRefNotRelease.SingleOrDefault( p => p.Key == min )
                            : null;
            if( worst == null || worst.Key == PackageQuality.Release )
            {
                Console.WriteLine( "Nothing prevent to choose the Release quality." );
            }
            else
            {
                var prev = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine( $"Best quality is {worst.Key} because of projects:" );
                foreach( var proj in worst )
                {
                    Console.WriteLine( "    => " + proj.p.Name + " caused by packages references " +
                        string.Join( ", ", proj.Item2.Select( p => p.ToString() ).ToArray() ) );
                }
                Console.ForegroundColor = prev;
            }

            foreach( var kv in c.PossibleVersions )
            {
                Console.Write( $"= {(int)kv.Key} - {kv.Key} => " );
                for( int i = 0; i < kv.Value.Count; i++ )
                {
                    CSVersion version = kv.Value[i];
                    var prev = Console.ForegroundColor;
                    Console.ForegroundColor = kv.Key != ReleaseLevel.None
                        ? version.PackageQuality > (worst?.Key ?? PackageQuality.Release) ? ConsoleColor.Red : ConsoleColor.Green
                        : ConsoleColor.White;
                    Console.Write( version.NormalizedText );
                    if( i < kv.Value.Count - 1 ) Console.Write( ", " );
                    Console.ForegroundColor = prev;
                }
                Console.WriteLine();
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
                while( (c.CanUsePreviouslyResolvedInfo ? "0123AX": "0123X").IndexOf( (a = Console.ReadKey().KeyChar) ) < 0 ) Console.Write( '\b' );
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
                    string line = ReadLine.Read();
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
        public bool OnAlreadyReleased( IActivityMonitor m, DependentSolution solution, CSVersion version, bool isContentTag )
        {
            Console.WriteLine( "=========" );
            Console.WriteLine( $"========  {solution.Solution.Name} is already released in version '{version}'." );
            return true;
        }

    }
}
