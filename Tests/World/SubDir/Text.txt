using CK.Text;
using LibGit2Sharp;
using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using static CK.Testing.MonitorTestHelper;

namespace CK.Env.FS.Tests
{
    public static class LocalTestHelper
    {
        static NormalizedPath _worldFolder;

        public static readonly string TestGitRepositoryUrl = "https://github.com/SimpleGitVersion/TestGitRepository.git";

        /// <summary>
        /// Gets a small world: that contains one Git repository (https://github.com/SimpleGitVersion/TestGitRepository.git)
        /// in the folder "TestGitRepository", a "Test.xml" file and directory with a copy of this file in it and
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
                    File.WriteAllText( _worldFolder + "/Test.xml", "<Content>Test.xml</Content>" );
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
