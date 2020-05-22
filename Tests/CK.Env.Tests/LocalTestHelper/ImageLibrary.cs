using CK.Core;
using CK.Text;
using FluentAssertions;
using System;
using System.Runtime.CompilerServices;
using System.Linq;

using static CK.Testing.MonitorTestHelper;
using NUnit.Framework.Internal;

namespace CK.Env.Tests.LocalTestHelper
{
    public static class ImageLibrary
    {
        public const string CKTestBuildStackName = "CKTest-Build";

        /// <summary>
        /// Helper to build an image, save it, then run test against it.
        /// Instantiate an image, then run a build action, then snapshot the image, and then run the test callback on it.
        /// </summary>
        /// <param name="testCallback">This action is called after the image was snapshoted.</param>
        /// <param name="refreshCache">Rebuild from scratch the base image, even if the image already exist.</param>
        /// <param name="parentBuilder">The action that build the image that we are based on.</param>
        /// <param name="buildAction">The action that build the new image.</param>
        /// <param name="newImageName">The name of the new image.</param>
        /// <returns>The path of the new image.</returns>
        static NormalizedPath ImageBuilderHelper<T>(
            Action<TestUniverse> testCallback,
            bool refreshCache,
            Func<T, bool, NormalizedPath> parentBuilder,
            Func<TestUniverse, TestUniverse> buildAction,
            [CallerMemberName] string newImageName = null )
        {
            TestUniverse universe = ImageManager.InstantiateImage( TestHelper.Monitor, parentBuilder, refreshCache );
            try
            {
                universe = buildAction( universe );
                var snapshotPath = universe.SnapshotState( newImageName );
                testCallback?.Invoke( universe );
                return snapshotPath;
            }
            finally
            {
                universe.Dispose();
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
        Func<TestUniverse, TestUniverse> buildAction,
        [CallerMemberName] string newImageName = null ) =>
            ImageBuilderHelper(
                ( universe ) => testCallback?.Invoke( universe, universe.EnsureWorldOpened( TestHelper.Monitor, worldName ) ),
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
        /// <param name="parentBuilder">The action that builds the image that we are based on.</param>
        /// <param name="buildAction">The action that build the new image.</param>
        /// <param name="newImageName">The name of the new image.</param>
        /// <returns>The path of the new image.</returns>
        static NormalizedPath ImageBuilderHelper<T>(
            string worldName,
            Action<TestUniverse, World> testCallback,
            bool refreshCache,
            Func<T, bool, NormalizedPath> parentBuilder,
            Func<TestUniverse, World, TestUniverse> buildAction,
            [CallerMemberName] string newImageName = null ) =>
                ImageBuilderHelper( worldName, testCallback,
                    refreshCache,
                    parentBuilder,
                    ( universe ) =>
                    {
                        universe.EnsureWorldOpened( TestHelper.Monitor, worldName );
                        var w = universe.UserHost.WorldSelector.CurrentWorld;
                        w.Should().NotBeNull();
                        return buildAction( universe, w );
                    },
                    newImageName );

        public static NormalizedPath minimal_solution_setup( Action<TestUniverse> testCallback, bool useless )
        {
            using( TestUniverse universe = ImageManager.InstantiateImage(
                m: TestHelper.Monitor,
                imagePath: ImageManager.SeedUniverseFolder.AppendPart( "minimal_project.zip" ) )
            )
            {
                universe.SeedInitialSetup( TestHelper.Monitor );
                var snapshotPath = universe.SnapshotState( nameof( minimal_solution_setup ) );
                testCallback?.Invoke( universe );
                return snapshotPath;
            }
        }

        public static NormalizedPath minimal_solution_open( Action<TestUniverse> testCallback, bool refreshCache ) =>
            ImageBuilderHelper<Action<TestUniverse>>( testCallback, refreshCache, minimal_solution_setup,
                universe =>
                {
                    universe.EnsureWorldOpened( TestHelper.Monitor, CKTestBuildStackName );
                    return universe;
                } );

        public static NormalizedPath minimal_solution_apply_settings( Action<TestUniverse> testCallback, bool refreshCache ) =>
            ImageBuilderHelper<Action<TestUniverse>>( testCallback, refreshCache, minimal_solution_open, universe =>
                universe.RunCommands( TestHelper.Monitor, CKTestBuildStackName, "**ApplySettings", true )
                    .CommitAll( TestHelper.Monitor, "Applied all settings.", CKTestBuildStackName ) );

        public static NormalizedPath minimal_solution_first_ci_build( Action<TestUniverse, World> testCallback, bool refreshCache ) =>
            ImageBuilderHelper<Action<TestUniverse>>( CKTestBuildStackName, testCallback, refreshCache, minimal_solution_apply_settings,
                ( universe, world ) =>
                {
                    world.GitRepositories.All( g => g.CheckCleanCommit( TestHelper.Monitor ) ).Should().BeTrue( "All repositories should be clean before AllBuild." );
                    world.AllBuild( TestHelper.Monitor ).Should().BeTrue( "AllBuild must be successful." );
                    world.GitRepositories.All( g => g.CheckCleanCommit( TestHelper.Monitor ) ).Should().BeTrue( "All repositories should be clean after AllBuild." );
                    return universe;
                } );

        public static NormalizedPath minimal_solution_switched_to_local( Action<TestUniverse, World> testCallback, bool refreshCache ) =>
            ImageBuilderHelper<Action<TestUniverse, World>>( CKTestBuildStackName, testCallback, refreshCache, minimal_solution_first_ci_build,
                ( universe, world ) =>
                {
                    world.SwitchToLocal( TestHelper.Monitor );
                    return universe;
                } );


        public static NormalizedPath minimal_solution_second_ci_build( Action<TestUniverse, World> testCallback, bool refreshCache ) =>
            ImageBuilderHelper<Action<TestUniverse, World>>( CKTestBuildStackName, testCallback, refreshCache, minimal_solution_first_ci_build,
                ( universe, world ) =>
                {
                    world.AllBuild( TestHelper.Monitor );
                    return universe;
                } );

        public static NormalizedPath another_minimal_solution_second_ci_build( Action<TestUniverse, World> testCallback, bool refreshCache ) =>
            ImageBuilderHelper<Action<TestUniverse, World>>( CKTestBuildStackName, testCallback, refreshCache, minimal_solution_first_ci_build,
                ( universe, world ) =>
                {
                    world.AllBuild( TestHelper.Monitor );
                    return universe;
                } );

        public static NormalizedPath full_apply_settings_randomly_applied( Action<TestUniverse> testCallback, bool refreshCache, int seed ) =>
            ImageBuilderHelper<Action<TestUniverse, World>>( testCallback, refreshCache, another_minimal_solution_second_ci_build,
                ( universe ) => universe.ApplySettingsAndCommitRandomly( TestHelper.Monitor, CKTestBuildStackName, seed ) );

        public static NormalizedPath full_apply_settings( Action<TestUniverse, World> testCallback, bool refreshCache ) =>
            ImageBuilderHelper<Action<TestUniverse, World>>( CKTestBuildStackName, testCallback, refreshCache, another_minimal_solution_second_ci_build,
                ( universe ) => universe.ApplySettings( TestHelper.Monitor, CKTestBuildStackName ) );

        public static NormalizedPath workstation_setup_helloworld_script_added( Action<TestUniverse, World> action, bool refreshCache ) =>
            ImageBuilderHelper<Action<TestUniverse, World>>( CKTestBuildStackName, action, refreshCache, full_apply_settings,
                       ( universe ) =>
                       {
                           var path = universe.GetWorldByName( CKTestBuildStackName ).Repo.Root.AppendPart( "workstation_setup_ran" );
                           universe.AddSetupScriptInStack( CKTestBuildStackName, $"Write-Host \"Hello World\";\nNew-Item {path};" );
                           return universe.RestartCKli( TestHelper.Monitor );
                       } );
    }
}
