using CK.Core;
using CK.Text;
using FluentAssertions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CK.Env.MSBuildSln.Tests
{
    [TestFixture]
    public class SolutionFileTests
    {
        readonly CommandRegister _commandRegister = new CommandRegister();
        readonly SecretKeyStore _keyStore = new SecretKeyStore();

        [Test]
        public void reading_this_solution_works()
        {
            using( var fs = new FileSystem( TestHelper.SolutionFolder, _commandRegister, _keyStore, new SimpleServiceContainer() ) )
            {
                var s = SolutionFile.Read( fs, TestHelper.Monitor, "CK-Env.sln", true );

                var folders = s.Children.OfType<SolutionFolder>();
                folders.Select( p => p.ProjectName ).Should().BeEquivalentTo( "Solution Items", "Tests", "Plugins" );
                folders.Single( p => p.ProjectName == "Solution Items" ).Items.Select( item => item.Path )
                    .Should().BeEquivalentTo(
                        ".editorconfig",
                        ".gitignore",
                        "nuget.config",
                        "README.md",
                        "Common/SharedKey.snk" );
            }
        }


        [Test]
        public void changing_TargetFrameworks()
        {
            var cache = new Dictionary<NormalizedPath, MSProjFile>();

            using( var fs = new FileSystem( TestHelper.TestProjectFolder, _commandRegister, _keyStore, new SimpleServiceContainer() ) )
            {
                var s = SolutionFile.Read( fs, TestHelper.Monitor, "Samples/SampleSolution.sln", true );
                s.IsDirty.Should().BeFalse();
                var p1 = s.MSProjects.Single( p => p.ProjectName == "P1" );
                p1.TargetFrameworks.Should().BeSameAs( MSProject.Savors.FindOrCreate( "netcoreapp2.1" ) );

                p1.SetTargetFrameworks( TestHelper.Monitor, MSProject.Savors.FindOrCreate( "netcoreapp3.1" ) );
                s.IsDirty.Should().BeTrue();
                s.Save( TestHelper.Monitor ).Should().BeTrue();
                s.IsDirty.Should().BeFalse();

                p1.SetTargetFrameworks( TestHelper.Monitor, MSProject.Savors.FindOrCreate( "netcoreapp2.1" ) );
                s.IsDirty.Should().BeTrue();
                s.Save( TestHelper.Monitor ).Should().BeTrue();
            }
        }
    }
}
