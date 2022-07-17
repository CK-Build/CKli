using CK.Core;
using FluentAssertions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using static CK.Testing.MonitorTestHelper;

namespace CK.Env.FS.Tests
{
    [TestFixture]
    public class MiscellaneousTests
    {
        [TestCase( "cmd.exe", "/C test", "test", "" )]
        [TestCase( "cmd.exe", "   /C test", "test", "" )]
        [TestCase( "cmd.exe", "/C test a", "test", "a" )]
        [TestCase( "cmd.exe", "/C test     a ", "test", "    a " )]
        [TestCase( "cmd.exe", "/C test a b", "test", "a b" )]
        [TestCase( "cmd.exe", "/C 'this is not supported' a b", "'this", "is not supported' a b" )]

        [TestCase( " cmd.exe", "/C test", null, null )]
        [TestCase( "cmd", "/C test", null, null )]
        public void adapt_string_filename_and_argument_onUnix(string fileName, string args, string? expectedFileName, string? expectedArgs )
        {
            ProcessStartInfo startInfo = new ProcessStartInfo() { FileName = fileName, Arguments = args };

            if( startInfo.Arguments == null ) startInfo.Arguments = string.Empty;
            else startInfo.Arguments = startInfo.Arguments.TrimStart();

            var m = TestHelper.Monitor;
            if( startInfo.FileName.Equals( "cmd.exe", StringComparison.OrdinalIgnoreCase ) && startInfo.Arguments.StartsWith( "/C " ) )
            {
                int idx = startInfo.Arguments.IndexOf( ' ', 3 );
                if( idx < 0 )
                {
                    startInfo.FileName = startInfo.Arguments.Substring( 3 );
                    startInfo.Arguments = String.Empty;
                }
                else
                {
                    startInfo.FileName = startInfo.Arguments.Substring( 3, idx - 3 );
                    startInfo.Arguments = startInfo.Arguments.Substring( idx + 1 );
                }
                m.Info( "Call to cmd.exe /C detected: since this cannot work on Unix platforms, this has been automatically adapted to directly call the command." );
                if( startInfo.FileName.Contains( '"' ) || startInfo.FileName.Contains( '\'' ) )
                {
                    m.Warn( "This adaptation is simple and naïve: the command name should not be quoted nor contain white escaped spaces. If this happens, please change the call to target the Unix command directly." );
                }
            }

            startInfo.FileName.Should().Be( expectedFileName ?? fileName );
            startInfo.Arguments.Should().Be( expectedArgs ?? args );
        }

    }
}
