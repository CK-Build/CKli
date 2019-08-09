using CK.Core;
using FluentAssertions;
using NUnit.Framework;
using System;
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

                s.Children.Should().HaveCount( 33, "There must be 33 projects!" );
                var folders = s.Children.OfType<SolutionFolder>();

                folders.Select( p => p.ProjectName ).Should().BeEquivalentTo( "Solution Items", "Tests" );

                folders.Single( p => p.ProjectName == "Solution Items" ).Items
                    .Select( item => item.Path )
                    // Skips .xmlfiles since these are changing a lot currently (worlds).
                    .Where( p => !p.EndsWith(".xml") )
                    .Should().BeEquivalentTo(
                        ".editorconfig",
                        ".gitignore",
                        "LocalWorldRootPathMapping.txt",
                        "nuget.config",
                        "Common/SharedKey.snk" );

                using( var w = new System.IO.StringWriter() )
                {
                    s.Write( w );
                    Console.WriteLine( w.ToString() );
                }
            }
        }

    }
}
