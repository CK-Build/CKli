using CK.Core;
using CK.Text;
using FluentAssertions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
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

        [Test]
        public void updating_dependencies_uses_the_ParsedText_Version_and_not_the_normalized_text()
        {
            var cache = new Dictionary<NormalizedPath, MSProjFile>();

            using( var fs = new FileSystem( TestHelper.TestProjectFolder, _commandRegister, _keyStore, new SimpleServiceContainer() ) )
            {
                var s = SolutionFile.Read( fs, TestHelper.Monitor, "Samples/SampleSolution.sln", true );
                var p1 = s.MSProjects.Single( p => p.ProjectName == "P1" );
                var dep = p1.Deps.Packages.Single( p => p.Package.Artifact.Name == "NetTopologySuite.IO.GeoJSON" );

                dep.Version.ToString().Should().Be( "1.15.6-rc.1", "There must be no transformation to short form." );
                dep.Version.Should().BeSameAs( dep.Version.AsCSVersion, "Because the string has been parsed as a CSVersion..." );
                dep.Version.AsCSVersion.IsLongForm.Should().BeTrue( "...and the long form has been identified." );

                int updateCount = p1.SetPackageReferenceVersion( TestHelper.Monitor, p1.TargetFrameworks, "NetTopologySuite.IO.GeoJSON", CSemVer.SVersion.Parse( "1.15.6-rc.2" ) );
                updateCount.Should().Be( 1 );

                p1.ProjectFile.Document.Root
                    .Elements( "ItemGroup" )
                        .Elements( "PackageReference" )
                        .Single( e => (string)e.Attribute( "Include" ) == "NetTopologySuite.IO.GeoJSON" )
                        .Attribute( "Version" ).Value.Should().Be( "1.15.6-rc.2", "The version should be the parsed one, not the normalized (short) form." );
            }
        }

    }
}
