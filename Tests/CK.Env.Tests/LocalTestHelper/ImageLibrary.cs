using CK.Text;
using FluentAssertions;
using System;
using System.IO;
using static CK.Testing.MonitorTestHelper;

namespace CK.Env.Tests.LocalTestHelper
{
    public static class ImageLibrary
    {
        /// <summary>
        /// Input: The base seed image.
        /// Process: CKli "EnsureStackDefinition". (Will clone the stack definition repo)
        /// </summary>
        /// <param name="action">Action effectued after the snapshot of the state. Change won't be saved.</param>
        /// <returns></returns>
        public static NormalizedPath minimal_solution_setup( Action<TestUniverse> action, bool refreshBaseImage )
        {
            using( TestUniverse universe = ImageManager.InstantiateImage( TestHelper.Monitor, ImageManager.SeedUniverseFolder.AppendPart( "minimal_project.zip" ) ) )
            {
                universe.UserHost.WorldStore.EnsureStackDefinition(
                    m: TestHelper.Monitor,
                    stackName: "CKTest-Build",
                    url: universe.StackBareGitPath,
                    isPublic: true,
                    mappedPath: universe.UserLocalDirectory
                );
                TestUniverse.PlaceHolderSwapEverything(
                    m: TestHelper.Monitor,
                    tempPath: universe.UniversePath,
                    oldString: TestUniverse.PlaceHolderString,
                    newString: universe.UniversePath
                );
                universe.UserHost.WorldStore.PullAll( TestHelper.Monitor ).Should().BeFalse();//The repo was previously cloned, pulling should do nothing.
                universe.UserHost.WorldSelector.Open( TestHelper.Monitor, "CKTest-Build" ).Should().BeTrue();
                var snapshotPath = universe.SnapshotState( nameof( minimal_solution_setup ) );
                action?.Invoke( universe );
                return snapshotPath;
            }
        }

        public static NormalizedPath minimal_solution_first_ci_build( Action<TestUniverse> action, bool refreshBaseImage )
        {
            using( var universe = ImageManager.InstantiateImage(
                m: TestHelper.Monitor,
                parentImageGenerator: minimal_solution_setup,
                refreshCache: refreshBaseImage ) )
            {
                universe.UserHost.WorldSelector.Open( TestHelper.Monitor, "CKTest-Build" ).Should().BeTrue();
                var w = universe.UserHost.WorldSelector.CurrentWorld;
                w.Should().NotBeNull();
                w.AllBuild( TestHelper.Monitor, true ).Should().BeTrue();
                var snapshotPath = universe.SnapshotState( nameof( minimal_solution_first_ci_build ) );
                action?.Invoke( universe );
                return snapshotPath;
            }
        }

        static NormalizedPath second_build( Action<TestUniverse> action, bool refreshBaseImage, string buildName )
        {
            using( var testHost = ImageManager.InstantiateImage(
                m: TestHelper.Monitor,
                parentImageGenerator: minimal_solution_first_ci_build,
                refreshCache: refreshBaseImage ) )
            {
                testHost.UserHost.WorldSelector.Open( TestHelper.Monitor, "CKTest-Build" ).Should().BeTrue();
                var w = testHost.UserHost.WorldSelector.CurrentWorld;
                w.Should().NotBeNull();
                w.AllBuild( TestHelper.Monitor, true ).Should().BeTrue();
                var snapshotPath = testHost.SnapshotState( buildName );
                action?.Invoke( testHost );
                return snapshotPath;
            }
        }

        public static NormalizedPath minimal_solution_second_ci_build( Action<TestUniverse> action, bool refreshBaseImage )
        {
            return second_build( action, refreshBaseImage, nameof( minimal_solution_second_ci_build ) );
        }

        public static NormalizedPath another_minimal_solution_second_ci_build( Action<TestUniverse> action, bool refreshCache )
        {
            return second_build( action, refreshCache, nameof( another_minimal_solution_second_ci_build ) );
        }
    }
}
