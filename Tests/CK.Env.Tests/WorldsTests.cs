using NUnit.Framework;
using FluentAssertions;

using static CK.Testing.MonitorTestHelper;
using CK.Env.Tests.LocalTestHelper;
using System.IO;
using System.Linq;
using System;
using CK.Text;
using System.Collections.Generic;
using System.Diagnostics;

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
            Assume.That( TestHelper.IsExplicitAllowed );
            ImageLibrary.minimal_solution_setup( ( universe ) => { }, TestHelper.IsExplicitAllowed );
        }

        [Test]
        public void a_simple_project_can_be_build_once()
        {
            Assume.That( TestHelper.IsExplicitAllowed );
            EnsureCallbackIsCalled(
                ( Action<TestUniverse> action, object[] parameters ) => ImageLibrary.minimal_solution_first_ci_build( action, (bool)parameters[0] ),
                ( universe ) =>
                {
                    var files = Directory.EnumerateFiles( universe.DevDirectory.Combine( "LocalFeed/CI" ) );
                    files.Should().HaveCount( 1 );
                    Path.GetFileName( files.Single() ).Should().Be( "CKTest.Code.Cake.0.1.1--0003-develop.nupkg" );
                },
                TestHelper.IsExplicitAllowed
            );
        }

        [Test]
        public void a_simple_project_can_be_build_a_second_time()
        {
            Assume.That( TestHelper.IsExplicitAllowed );
            ImageLibrary.minimal_solution_second_ci_build(
            ( universe ) =>
            {

            }, TestHelper.IsExplicitAllowed );
        }

        [Test]
        public void dll_should_not_change_after_rebuild()
        {
            Assume.That( TestHelper.IsExplicitAllowed );
            ImageLibrary.minimal_solution_second_ci_build( universe => { }, TestHelper.IsExplicitAllowed );//We are now sure this image, and it's base exist.
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
            Assume.That( TestHelper.IsExplicitAllowed );
            bool isExplicit = TestHelper.IsExplicitAllowed;
            var imageA = ImageLibrary.minimal_solution_second_ci_build( null, isExplicit );
            var imageB = ImageLibrary.another_minimal_solution_second_ci_build( null, isExplicit );
            using( var compare = ImageManager.CompareBuildedImages( imageA, imageB ) )
            {
                compare.AExceptB.Where( p => IsFileThatShouldBeDeterministic( p.FullName ) ).Should().BeEmpty();
            }
        }

        [TestCase( 42 )]
        [TestCase( 3712 )]
        [TestCase( -1 )]
        public void running_apply_settings_does_not_throw( int seed )
        {
            Assume.That( TestHelper.IsExplicitAllowed );
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
            Assume.That( TestHelper.IsExplicitAllowed );
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
