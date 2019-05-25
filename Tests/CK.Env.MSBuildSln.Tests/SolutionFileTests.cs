using CK.Core;
using FluentAssertions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static CK.Testing.MonitorTestHelper;

namespace CK.Env.MSBuildSln.Tests
{
    [TestFixture]
    public class SolutionFileTests
    {
        class KeyStore : ISecretKeyStore
        {
            public string GetSecretKey( IActivityMonitor m, string name, bool throwOnEmpty, string message = null )
            {
                throw new NotImplementedException();
            }
        }

        readonly CommandRegister _commandRegister = new CommandRegister();
        readonly ISecretKeyStore _keyStore = new KeyStore();

        [Test]
        public void reading_this_solution_works()
        {
            using( var fs = new FileSystem( TestHelper.SolutionFolder, _commandRegister, _keyStore, new SimpleServiceContainer() ) )
            {
                var s = SolutionFile.Read( fs, TestHelper.Monitor, "CK-Env.sln", true );

                s.Children.Should().HaveCount( 26, "There must be 26 projects!" );
                var folders = s.Children.OfType<SolutionFolder>();

                folders.Select( p => p.ProjectName ).Should().BeEquivalentTo( "Solution Items", "Tests" );

                folders.Single( p => p.ProjectName == "Solution Items" ).Items
                    .Select( item => item.Path )
                    .Should().BeEquivalentTo(
                        ".editorconfig",
                        ".gitignore",
                        "A1Test-World.xml",
                        "A2Test-World.xml",
                        "CK-World.xml",
                        "LocalWorldRootPathMapping.txt",
                        "nuget.config",
                        "SC-World.xml",
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
