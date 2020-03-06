using CK.Core;
using CK.Text;
using FluentAssertions;
using System;
using System.Runtime.CompilerServices;

using static CK.Testing.MonitorTestHelper;
namespace CK.Env.Tests.LocalTestHelper
{
    public static class ImageLibrary
    {
        const string _cktestBuildStackName = "CKTest-Build";

        /// <summary>
        /// Helper to build an image, save it, then run test against it.
        /// Instantiate an image, then run a build action, then snapshot the image, and then run the test callback on it.
        /// </summary>
        /// <param name="testCallback">This action is called after the image was snapshoted.</param>
        /// <param name="refreshCache">Rebuild from scratch the base image, even if the image already exist.</param>
        /// <param name="parentBuilder">The action that build that build the image that we are based on.</param>
        /// <param name="buildAction">The action that build the new image.</param>
        /// <param name="newImageName">The name of the new image.</param>
        /// <returns>The path of the new image.</returns>
        static NormalizedPath ImageBuilderHelper<T>(
            Action<TestUniverse> testCallback,
            bool refreshCache,
            Func<T, bool, NormalizedPath> parentBuilder,
            Action<TestUniverse> buildAction,
            [CallerMemberName] string newImageName = null )
        {
            using( TestUniverse universe = ImageManager.InstantiateImage( TestHelper.Monitor, parentBuilder, refreshCache ) )
            {
                buildAction( universe );
                var snapshotPath = universe.SnapshotState( newImageName );
                testCallback?.Invoke( universe );
                return snapshotPath;
            }
        }


        /// <summary>
        /// Helper to build an image, save it, then run test against it.
        /// Instantiate an image, then run a build action, then snapshot the image, and then run the test callback on it.
        /// </summary>
        /// <param name="testCallback">This action is called after the image was snapshoted.</param>
        /// <param name="refreshCache">Rebuild from scratch the base image, even if the image already exist.</param>
        /// <param name="parentBuilder">The action that build that build the image that we are based on.</param>
        /// <param name="buildAction">The action that build the new image.</param>
        /// <param name="newImageName">The name of the new image.</param>
        /// <returns>The path of the new image.</returns>
        static NormalizedPath ImageBuilderHelper<T>(
            string worldName,
            Action<TestUniverse, World> testCallback,
            bool refreshCache,
            Func<T, bool, NormalizedPath> parentBuilder,
            Action<TestUniverse> buildAction,
            [CallerMemberName] string newImageName = null ) =>
                ImageBuilderHelper(
                    ( universe ) => testCallback?.Invoke( universe, universe.EnsureWorldOpened( worldName ) ),
                    refreshCache,
                    parentBuilder,
                    buildAction,
                    newImageName );

        /// <summary>
        /// Helper to build an image, save it, then run test against it.
        /// Instantiate an image, then run a build action, then snapshot the image, and then run the test callback on it.
        /// </summary>
        /// <param name="testCallback">This action is called after the image was snapshoted.</param>
        /// <param name="refreshCache">Rebuild from scratch the base image, even if the image already exist.</param>
        /// <param name="parentBuilder">The action that build that build the image that we are based on.</param>
        /// <param name="buildAction">The action that build the new image.</param>
        /// <param name="newImageName">The name of the new image.</param>
        /// <returns>The path of the new image.</returns>
        static NormalizedPath ImageBuilderHelper<T>(
            string worldName,
            Action<TestUniverse, World> testCallback,
            bool refreshCache,
            Func<T, bool, NormalizedPath> parentBuilder,
            Action<TestUniverse, World> buildAction,
            [CallerMemberName] string newImageName = null ) =>
                ImageBuilderHelper( worldName, testCallback,
                    refreshCache,
                    parentBuilder,
                    ( universe ) =>
                    {
                        universe.EnsureWorldOpened( worldName );
                        var w = universe.UserHost.WorldSelector.CurrentWorld;
                        w.Should().NotBeNull();
                        buildAction( universe, w );
                    },
                    newImageName );

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

        public static NormalizedPath minimal_solution_open( Action<TestUniverse> action, bool refreshCache ) =>
            ImageBuilderHelper<Action<TestUniverse>>( action, refreshCache, minimal_solution_setup,
                ( universe ) => universe.EnsureWorldOpened( _cktestBuildStackName ) );

        public static NormalizedPath minimal_solution_add_ccb( Action<TestUniverse> action, bool refreshCache ) =>
            ImageBuilderHelper<Action<TestUniverse>>( action, refreshCache, minimal_solution_open, ( universe ) =>
            {
                universe.ApplyWithFilter( TestHelper.Monitor, _cktestBuildStackName, "*CodeCakeBuilder*Apply*" );
                universe.CommitAll( TestHelper.Monitor, _cktestBuildStackName );
            } );

        public static NormalizedPath minimal_solution_first_ci_build( Action<TestUniverse, World> testCallback, bool refreshCache ) =>
            ImageBuilderHelper<Action<TestUniverse>>( _cktestBuildStackName, testCallback, refreshCache, minimal_solution_add_ccb,
                ( universe, world ) => world.AllBuild( TestHelper.Monitor ) );

        public static NormalizedPath minimal_solution_switched_to_local( Action<TestUniverse, World> testCallback, bool refreshCache ) =>
            ImageBuilderHelper<Action<TestUniverse, World>>( _cktestBuildStackName, testCallback, refreshCache, minimal_solution_first_ci_build,
                ( universe, world ) => world.SwitchToLocal( TestHelper.Monitor ) );


        public static NormalizedPath minimal_solution_second_ci_build( Action<TestUniverse, World> action, bool refreshCache ) =>
            ImageBuilderHelper<Action<TestUniverse, World>>( _cktestBuildStackName, action, refreshCache, minimal_solution_first_ci_build,
                ( universe, world ) => world.AllBuild( TestHelper.Monitor ) );

        public static NormalizedPath another_minimal_solution_second_ci_build( Action<TestUniverse, World> action, bool refreshCache ) =>
            ImageBuilderHelper<Action<TestUniverse, World>>( _cktestBuildStackName, action, refreshCache, minimal_solution_first_ci_build,
                ( universe, world ) => world.AllBuild( TestHelper.Monitor ) );

        public static NormalizedPath full_apply_settings_randomly_applied( Action<TestUniverse> action, bool refreshCache, int seed ) =>
            ImageBuilderHelper<Action<TestUniverse, World>>( action, refreshCache, another_minimal_solution_second_ci_build,
                ( universe ) => universe.ApplyRandomly( TestHelper.Monitor, _cktestBuildStackName, seed ) );

        public static NormalizedPath full_apply_settings( Action<TestUniverse> action, bool refreshCache ) =>
            ImageBuilderHelper<Action<TestUniverse, World>>( action, refreshCache, another_minimal_solution_second_ci_build,
                ( universe ) => universe.ApplyAll( TestHelper.Monitor, _cktestBuildStackName ) );
    }
}
