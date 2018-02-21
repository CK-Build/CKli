using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LibGit2Sharp;
using NUnit.Framework;
using System.Runtime.CompilerServices;
using CK.Core;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;
using CK.Text;

namespace CK.Env.Tests
{
    static class LocalTestHelper
    {
        static NormalizedPath _worldFolder;

        public static NormalizedPath XmlInputFolder => TestHelper.SolutionFolder.Combine( "Tests/CK.Env.Tests/XmlInput" );

        public static XElement LoadXmlInput( string name )
        {
            var p = Path.Combine( XmlInputFolder, name ) + ".xml";
            return XDocument.Load( p ).Root;
        }

        /// <summary>
        /// Gets a small world: that contains one Git repository (), a copy of the
        /// solution KnownWorld.xml a directory with a compy of this file int it and
        /// an empty directory.
        /// </summary>
        public static NormalizedPath WorldFolder
        {
            get
            {
                if( _worldFolder == null )
                {
                    _worldFolder = TestHelper.SolutionFolder.Combine( "Tests/World" );
                    var gitRepo = _worldFolder.Combine( "TestGitRepository" );
                    var gitPath = gitRepo.AppendPart( ".git" );
                    if( !Directory.Exists( gitPath ) )
                    {
                        // Let any exceptions be thrown here: if we can't have a copy of the test repository, it 
                        // is too risky to Assume(false).
                        Directory.CreateDirectory( gitRepo );
                        gitPath = Repository.Clone( "https://github.com/SimpleGitVersion/TestGitRepository.git", gitRepo );
                    }
                    try
                    {
                        using( var r = new Repository( gitRepo ) )
                        {
                            Commands.Fetch( r, "origin", Enumerable.Empty<string>(), new FetchOptions() { TagFetchMode = TagFetchMode.All }, "Testing." );
                        }
                    }
                    catch( LibGit2SharpException ex )
                    {
                        // Fetch fails. We don't care.
                        Console.WriteLine( "Warning: Fetching the TestGitRepository (https://github.com/SimpleGitVersion/TestGitRepository.git) failed. Check the internet connection. Error: {0}.", ex.Message );
                    }
                    File.Copy( TestHelper.SolutionFolder.AppendPart( "KnownWorld.xml" ), WorldFolder.AppendPart( "KnownWorld.xml" ), true );
                    Directory.CreateDirectory( _worldFolder + "/SubDir" );
                    File.Copy( ThisFile(), _worldFolder + "/SubDir/Text.txt", true );
                    Directory.CreateDirectory( _worldFolder + "/EmptyDir" );
                }
                return _worldFolder;
            }
        }

        static string ThisFile( [CallerFilePath]string p = null ) => p;

    }
}
