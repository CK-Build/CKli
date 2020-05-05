using CK.Core;
using CK.Env.Tests.LocalTestHelper;
using FluentAssertions;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CK.Env.Tests
{
    [TestFixture]
    public class WorldsTests
    {
        bool IsFileThatShouldBeDeterministic( string dllPath )
        {
            return dllPath.EndsWith( ".dll" ) && !dllPath.EndsWith( "CodeCakeBuilder.dll" ) && !dllPath.Contains( "ZeroBuild" );
        }

        static U EnsureCallbackIsCalled<T, U>( Func<Action<T>, object[], U> methodToCheck, Action<T> callbackThatMustBeCalled, params object[] parameters )
        {
            bool called = false;
            U result = methodToCheck( ( T parameter ) =>
            {
                called = true;
                callbackThatMustBeCalled( parameter );
            }, parameters );
            called.Should().BeTrue();
            return result;
        }


        [Test]
        public void a_simple_project_can_be_setup()
        {
            ImageLibrary.minimal_solution_setup( ( universe ) => { }, TestHelper.IsExplicitAllowed );
        }

        [Test]
        public void a_simple_project_can_be_opened()
        {
            ImageLibrary.minimal_solution_open( _ => { }, TestHelper.IsExplicitAllowed );
        }

        [Test]
        public void Exceptions_raised_by_Commands_stop_the_test_on_error()
        {
            ImageLibrary.minimal_solution_open( universe =>
            {
                var world = universe.EnsureWorldOpened( "CKTest-Build" );
                GitPluginSampleMock.ThrowOnExecuteSomething = true;
                try
                {
                    universe.Invoking( u => u.RunCommands( TestHelper.Monitor, world.WorldName.FullName, "**MockPlugin/ExecuteSomething" ) )
                                .Should().Throw<Exception>().WithMessage( "Mock.ExecuteSomething" );
                }
                finally
                {
                    GitPluginSampleMock.ThrowOnExecuteSomething = false;
                }
            }, TestHelper.IsExplicitAllowed );
        }

        [Test]
        public void a_simple_project_can_apply_settings()
        {
            ImageLibrary.minimal_solution_apply_settings( ( universe ) => { }, TestHelper.IsExplicitAllowed );
        }

        [Test]
        public void a_simple_project_can_be_built_once()
        {
            EnsureCallbackIsCalled( (Action<(TestUniverse, World)> action, object[] parameters) =>
                ImageLibrary.minimal_solution_first_ci_build(
                    ( universe, world ) => action( (universe, world) ),
                    (bool)parameters[0] ),
                    arg =>
                    {
                        var files = Directory.EnumerateFiles( arg.Item1.DevDirectory.Combine( "LocalFeed/CI" ) );
                        files.Should().HaveCount( 1 );
                        Path.GetFileName( files.Single() ).Should().Be( "CKTest.Code.Cake.0.1.1--0007-develop.nupkg" );
                    },
                    TestHelper.IsExplicitAllowed
                );
        }

        [Test]
        public void a_simple_project_can_be_built_a_second_time()
        {
            ImageLibrary.minimal_solution_second_ci_build( ( universe, world ) =>
            {
                var files = Directory.EnumerateFiles( universe.DevDirectory.Combine( "LocalFeed/CI" ) );
                files.Should().HaveCount( 1 ); //We didn't made any modification: the version should not change.
                Path.GetFileName( files.Single() ).Should().Be( "CKTest.Code.Cake.0.1.1--0007-develop.nupkg" );
            }, TestHelper.IsExplicitAllowed );
        }

        [Test]
        public void dll_should_not_change_after_rebuild()
        {
            ImageLibrary.minimal_solution_second_ci_build( (universe, world) => { }, TestHelper.IsExplicitAllowed ); //We are now sure this image, and it's base exist.
            using( var compare = ImageManager.CompareBuildedImages(
                nameof( ImageLibrary.minimal_solution_first_ci_build ),
                nameof( ImageLibrary.minimal_solution_second_ci_build ) ) )
            {
                compare.AExceptB.Where( p => IsFileThatShouldBeDeterministic( p.FullName ) ).Should().BeEmpty();
            }
        }

        [Test]
        public void building_the_same_sln_from_two_dirs_should_be_deterministic()
        {
            bool isExplicit = TestHelper.IsExplicitAllowed;
            var imageA = ImageLibrary.minimal_solution_second_ci_build( null, isExplicit );
            var imageB = ImageLibrary.another_minimal_solution_second_ci_build( null, isExplicit );
            using( var compare = ImageManager.CompareBuiltImages( imageA, imageB ) )
            {
                compare.AExceptB.Where( p => IsFileThatShouldBeDeterministic( p.FullName ) ).Should().BeEmpty();
            }
        }

        [TestCase( 42 )]
        [TestCase( 3712 )]
        [TestCase( -1 )]
        public void running_apply_settings_does_not_throw( int seed )
        {
            if( seed == -1 )
            {
                seed = new Random().Next();
            }
            bool isExplicit = TestHelper.IsExplicitAllowed;
            ImageLibrary.full_apply_settings( null, isExplicit );
            ImageLibrary.full_apply_settings_randomly_applied( null, isExplicit, seed );
        }

        [TestCase( 42 )]
        [TestCase( 3712 )]
        [TestCase( -1 )]
        public void apply_settings_should_be_idempotent( int seed )
        {
            if( seed == -1 )
            {
                seed = new Random().Next();
            }
            bool isExplicit = TestHelper.IsExplicitAllowed;
            ImageLibrary.full_apply_settings( null, isExplicit );
            ImageLibrary.full_apply_settings_randomly_applied( null, isExplicit, seed );
            using( var compare = ImageManager.CompareBuildedImages(
               nameof( ImageLibrary.full_apply_settings ),
               nameof( ImageLibrary.full_apply_settings_randomly_applied ) ) )
            {
                compare.AExceptB.Where( p => p.FullName.EndsWith( ".git" ) );
            }
        }
    }
}
