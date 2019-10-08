using NUnit.Framework;
using FluentAssertions;

using static CK.Testing.MonitorTestHelper;
using CK.Env.Tests.LocalTestHelper;
using System.IO;
using System.Linq;
using System;

namespace CK.Env.Tests
{
    [TestFixture]
    public class WorldsTests
    {
        bool IsFileThatShouldBeDeterministic( string dllPath )
        {
            return dllPath.EndsWith( ".dll" ) && !dllPath.EndsWith( "CodeCakeBuilder.dll" ) && !dllPath.Contains( "ZeroBuild" );
        }



        [Test]
        public void a_simple_project_can_be_setup() => ImageLibrary.minimal_solution_setup( ( universe ) => { }, TestHelper.IsExplicitAllowed );

        [Test]
        public void a_simple_project_can_be_build_once()
        {
            bool haveRun = false;
            ImageLibrary.minimal_solution_first_ci_build(
            ( universe ) =>
            {
                var files = Directory.EnumerateFiles( universe.DevDirectory.Combine( "LocalFeed/CI" ) );
                files.Should().HaveCount( 1 );
                Path.GetFileName(files.Single()).Should().Be( "CKTest.Code.Cake.0.1.1--0003-develop.nupkg" );
                haveRun = true;
            }, TestHelper.IsExplicitAllowed );
            haveRun.Should().BeTrue();
        }

        [Test]
        public void a_simple_project_can_be_build_a_second_time() => ImageLibrary.minimal_solution_second_ci_build(
            ( universe ) =>
            {

            }, TestHelper.IsExplicitAllowed );


        [Test]
        public void dll_should_not_change_after_rebuild()
        {
            ImageLibrary.minimal_solution_second_ci_build( universe => { }, TestHelper.IsExplicitAllowed );//We are now sure this image, and it's base exist.
            using( var compare = ImageManager.CompareBuildedImages(
                nameof( ImageLibrary.minimal_solution_first_ci_build ),
                nameof( ImageLibrary.minimal_solution_second_ci_build ) ) )
            {
                compare.AExceptB.Where( p => IsFileThatShouldBeDeterministic( p.FullName ) ).Should().BeEmpty();
            }
        }

        [Test]
        public void building_the_same_sln_from_two_dirs_should_be_deterministic()//Terrible name but i had no other idea.
        {
            bool isExplicit = TestHelper.IsExplicitAllowed;
            var imageA = ImageLibrary.minimal_solution_second_ci_build( null, isExplicit );
            var imageB = ImageLibrary.another_minimal_solution_second_ci_build( null, isExplicit );
            using( var compare = ImageManager.CompareBuildedImages( imageA, imageB ) )
            {
                compare.AExceptB.Where( p => IsFileThatShouldBeDeterministic( p.FullName ) ).Should().BeEmpty();
            }
        }
    }
}
