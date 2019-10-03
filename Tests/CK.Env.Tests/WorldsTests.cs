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

        [OneTimeSetUp]
        public void Init()
        {
            TestHelper.LogToConsole = true;
        }

        [Test]
        public void a_simple_project_can_be_build()
        {
            using( var testHost = ImageManager.InstantiateImageFromSeed( TestHelper.Monitor ) )
            {
                testHost.UserHost.WorldStore.EnsureStackDefinition( TestHelper.Monitor, "CKTest-Build", testHost.StackBareGitPath, true, testHost.UserLocalDirectory );
                TestUniverse.PlaceHolderSwapEverything( TestHelper.Monitor, testHost.TempPath, ImageManager.PlaceHolderString, testHost.TempPath );
                testHost.UserHost.WorldStore.PullAll( TestHelper.Monitor ).Should().BeFalse();//The repo was previously cloned, pulling should do nothing.
                testHost.UserHost.WorldSelector.Open( TestHelper.Monitor, "CKTest-Build" ).Should().BeTrue();
                var w = testHost.UserHost.WorldSelector.CurrentWorld;
                w.Should().NotBeNull();
                w.AllBuild( TestHelper.Monitor ).Should().BeTrue();
            }
            var cachePath = ImageManager.GetImagePath( nameof( a_simple_project_can_be_build ), false, true );
        }

        [Test]
        public void dll_should_not_change_after_rebuild()
        {
            using( var testHost = ImageManager.InstantiateAndGenerateImageIfNeeded( TestHelper.Monitor, a_simple_project_can_be_build ) )
            {
                testHost.UserHost.WorldSelector.Open( TestHelper.Monitor, "CKTest-Build" ).Should().BeTrue();
                var w = testHost.UserHost.WorldSelector.CurrentWorld;
                w.Should().NotBeNull();
                w.AllBuild( TestHelper.Monitor, true ).Should().BeTrue();
            }
            using( var compare = ImageManager.CompareBuildedImages(
                nameof( dll_should_not_change_after_rebuild ),
                nameof( a_simple_project_can_be_build ) ) )
            {
                compare.AExceptB.Where( p => IsFileThatShouldBeDeterministic( p.FullName ) ).Should().BeEmpty();
            }
        }

        [Test]
        public void non_incremental_build_should_be_deterministic()//Terrible name but i had no other idea.
        {
            var cachePath = ImageManager.GetImagePath( nameof( a_simple_project_can_be_build ), false, true );
            if( !File.Exists( cachePath ) )
            {
                a_simple_project_can_be_build();
            }
            if( !File.Exists( cachePath ) ) throw new InvalidOperationException();
            var backupName = nameof( a_simple_project_can_be_build ) + "_backup";
            var firstRunImagePath = ImageManager.GetImagePath( backupName, false, true );
            File.Delete( firstRunImagePath );
            File.Move( cachePath, firstRunImagePath );
            a_simple_project_can_be_build();
            using( var compare = ImageManager.CompareBuildedImages( backupName, nameof( a_simple_project_can_be_build ) ) )
            {
                compare.AExceptB.Where( p => IsFileThatShouldBeDeterministic( p.FullName ) ).Should().BeEmpty();
            }
        }
    }
}
