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

namespace CK.Env.Tests
{
    static class TestHelper
    {
        static string _solutionFolder;
        static string _worldFolder;

        static IActivityMonitor _monitor;
        static ActivityMonitorConsoleClient _console;

        static TestHelper()
        {
            ActivityMonitor.DefaultFilter = LogFilter.Debug;
            _monitor = new ActivityMonitor();
            // Do not pollute the console by default... LogsToConsole does the job.
            _console = new ActivityMonitorConsoleClient();
            LogsToConsole = true;
        }

        public static IActivityMonitor Monitor => _monitor;

        public static bool LogsToConsole
        {
            get { return _monitor.Output.Clients.Contains( _console ); }
            set
            {
                if( value ) _monitor.Output.RegisterUniqueClient( c => c == _console, () => _console );
                else _monitor.Output.UnregisterClient( _console );
            }
        }

        public static string SolutionFolder
        {
            get
            {
                if( _solutionFolder == null ) InitalizePaths();
                return _solutionFolder;
            }
        }

        public static string XmlInputFolder => Path.Combine( SolutionFolder, "Tests", "CK.Env.Tests", "XmlInput" );

        public static XElement LoadXmlInput( string name )
        {
            var p = Path.Combine( XmlInputFolder, name ) + ".xml";
            return XDocument.Load( p ).Root;
        }

        public static string WorldFolder
        {
            get
            {
                if( _worldFolder == null )
                {
                    _worldFolder = Path.Combine( SolutionFolder, "Tests", "World" );
                    var gitRepo = Path.Combine( _worldFolder, "TestGitRepository" );
                    string gitPath = gitRepo + @"\.git";
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
                    File.Copy( SolutionFolder + "/KnownWorld.xml", WorldFolder + "/KnownWorld.xml", true );
                    Directory.CreateDirectory( _worldFolder + "/SubDir" );
                    File.Copy( ThisFile(), _worldFolder + "/SubDir/Text.txt", true );
                    Directory.CreateDirectory( _worldFolder + "/EmptyDir" );
                }
                return _worldFolder;
            }
        }

        static void InitalizePaths()
        {
            string p = ThisFile();
            do
            {
                p = Path.GetDirectoryName( p );
            }
            while( !Directory.Exists( Path.Combine( p, ".git" ) ) );
            _solutionFolder = p;
        }

        static string ThisFile( [CallerFilePath]string p = null ) => p;

    }
}
