using CK.Text;
using FluentAssertions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using static CK.Testing.MonitorTestHelper;

namespace CK.SimpleKeyVault.Tests
{
    public class SimpleKeyVaultTests
    {
        static readonly NormalizedPath PreviousVersionsFolder = TestHelper.TestProjectFolder.AppendPart( "PreviousVersions" );

        // Don't change this since this is the content of previous versions!
        static Dictionary<string, string> ReferenceDictionary = new Dictionary<string, string> {
                { "ThisIsAKey", "a1" },
                { "There_Must_be::no-white/space|in$it...", "What a key!" },
                { "Hello", "world" },
                { "Hi", null },
                { "Hi2", "" },
                { "It", @"Works!\r\twell... There can be \r\nmultiple lines in the \u0000 (and any unicode chars) values..." }
            };


        [Test]
        public void simple_crypt_test()
        {
            var c = KeyVault.EncryptValuesToString( ReferenceDictionary, "A passphrase" );
            var read = KeyVault.DecryptValues( c, "A passphrase" );
            read.Should().BeEquivalentTo( ReferenceDictionary );
        }

        [TestCase(1)]
        public void read_previous_version( int version )
        {
            var fName = PreviousVersionsFolder.AppendPart( $"Version{version}.txt" );
            var read = KeyVault.DecryptValues( File.ReadAllText( fName ), "A passphrase" );
            read.Should().BeEquivalentTo( ReferenceDictionary );
        }

        [Test]
        public void bad_password_throws_an_InvalidDataException()
        {
            var c = KeyVault.EncryptValuesToString( ReferenceDictionary, "A passphrase" );
            Assert.Throws<InvalidDataException>( () => KeyVault.DecryptValues( c, "bad password" ) );
        }

        [Test]
        public void keys_can_be_removed()
        {
            var vals = new Dictionary<string, string> {
                { "ThisKeyWillBeRemoved", "qsmlk" },
                { "Hello", "world" },
                { "Hi", null },
                { "ThisWillAlsoBeRemoved", "this value will not be here." },
                { "It", @"Works! well.." }
            };
            // We remove the keys from the text crypted file (by emptying the declaration lines).
            var c = KeyVault.EncryptValuesToString( vals, "A passphrase" );
            c = c.Replace( "ThisKeyWillBeRemoved", "" )
                 .Replace( "ThisWillAlsoBeRemoved", "" );

            // Removing the keys from the initial dictionary: 
            vals.Remove( "ThisKeyWillBeRemoved" );
            vals.Remove( "ThisWillAlsoBeRemoved" );

            // Decryption doesn't return these removed lines!
            var vals2 = KeyVault.DecryptValues( c, "A passphrase" );
            vals2.Should().BeEquivalentTo( vals );
        }

        [TestCase( "" )]
        [TestCase( " " )]
        [TestCase( "a b" )]
        [TestCase( "\r" )]
        [TestCase( "\n" )]
        [TestCase( "x\ny" )]
        public void invalid_keys( string k )
        {
            var vals = new Dictionary<string, string> {
                { "A", "a1" },
                { k, "a2" }
            };
            Assert.Throws<ArgumentException>( () => KeyVault.EncryptValuesToString( vals, "A passphrase" ) );
        }
    }
}
