using CK.Text;
using System;
using System.Runtime.CompilerServices;

using static CK.Testing.MonitorTestHelper;
namespace CK.Env.Tests.LocalTestHelper
{
    public static class ImageLibrary
    {

        static NormalizedPath ImageBuilderHelper(
            Action<TestUniverse> action,
            bool refreshCache,
            Func<Action<TestUniverse>, bool, NormalizedPath> parentBuilder,
            Action<TestUniverse> buildAction,
            [CallerMemberName] string callerMemberName = null )
        {
            using( TestUniverse universe = ImageManager.InstantiateImage( TestHelper.Monitor, parentBuilder, refreshCache ) )
            {
                buildAction( universe );
                var snapshotPath = universe.SnapshotState( callerMemberName );
                action?.Invoke( universe );
                return snapshotPath;
            }
        }

        public static NormalizedPath minimal_solution_setup( Action<TestUniverse> action, bool useless )
        {
            using( TestUniverse universe = ImageManager.InstantiateImage(
                m: TestHelper.Monitor,
                imagePath: ImageManager.SeedUniverseFolder.AppendPart( "minimal_project.zip" ) )
            )
            {
                universe.SeedInitialSetup( TestHelper.Monitor );
                var snapshotPath = universe.SnapshotState( nameof( minimal_solution_setup ) );
                action?.Invoke( universe );
                return snapshotPath;
            }
        }

        public static NormalizedPath minimal_solution_first_ci_build( Action<TestUniverse> action, bool refreshCache )
        {
            return ImageBuilderHelper( action, refreshCache, minimal_solution_setup, ( universe ) =>
             {
                 universe.AllBuild( TestHelper.Monitor, "CKTest-Build" );
             } );
        }

        public static NormalizedPath minimal_solution_second_ci_build( Action<TestUniverse> action, bool refreshCache )
        {
            return ImageBuilderHelper( action, refreshCache, minimal_solution_first_ci_build, ( universe ) =>
            {
                universe.AllBuild( TestHelper.Monitor, "CKTest-Build" );
            } );
        }

        public static NormalizedPath another_minimal_solution_second_ci_build( Action<TestUniverse> action, bool refreshCache )
        {
            return ImageBuilderHelper( action, refreshCache, minimal_solution_first_ci_build, ( universe ) =>
            {
                universe.AllBuild( TestHelper.Monitor, "CKTest-Build" );
            } );
        }

        public static NormalizedPath full_apply_settings_randomly_applied( Action<TestUniverse> action, bool refreshCache, int seed )
        {
            return ImageBuilderHelper( action, refreshCache, another_minimal_solution_second_ci_build, ( universe ) =>
            {
                universe.ApplyRandomly( TestHelper.Monitor, "CKTest-Build", seed );
            } );
        }

        public static NormalizedPath full_apply_settings( Action<TestUniverse> action, bool refreshCache )
        {
            return ImageBuilderHelper( action, refreshCache, another_minimal_solution_second_ci_build, ( universe ) =>
             {
                 universe.ApplyAll( TestHelper.Monitor, "CKTest-Build" );
             } );
        }
    }
}
