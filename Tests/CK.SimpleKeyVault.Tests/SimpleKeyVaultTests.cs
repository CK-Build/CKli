using FluentAssertions;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace CK.SimpleKeyVault.Tests
{
    public class SimpleKeyVaultTests
    {
        [Test]
        public void simple_crypt_test()
        {
            var vals = new Dictionary<string,string> {
                { "A", "a1" },
                { "Hello", "world" },
                { "Hi", null },
                { "Hi2", "" },
                { "It", @"Works!
                          well.." }
            };
            var c = KeyVault.EncryptValuesToString( vals, "A passphrase" );
            var vals2 = KeyVault.DecryptValues( c, "A passphrase" );
            vals2.Should().BeEquivalentTo( vals );

            Assert.Throws<System.Security.Cryptography.CryptographicException>(
                () => KeyVault.DecryptValues( c, "bad password" ) );
        }

        [Test]
        public void keys_can_be_removed()
        {
            var vals = new Dictionary<string, string> {
                { "ThisKeyWillBeRemoved", "qsmlk" },
                { "Hello", "world" },
                { "Hi", null },
                { "ThisWillAlsoBeRemoved", "this value will not be here." },
                { "It", @"Works!
                          well.." }
            };
            var c = KeyVault.EncryptValuesToString( vals, "A passphrase" );
            c = c.Replace( "ThisKeyWillBeRemoved", "" )
                 .Replace( "ThisWillAlsoBeRemoved", "" );

            var vals2 = KeyVault.DecryptValues( c, "A passphrase" );
            vals.Remove( "ThisKeyWillBeRemoved" );
            vals.Remove( "ThisWillAlsoBeRemoved" );
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
            var vals = new Dictionary<string,string> {
                { "A", "a1" },
                { k, "a2" }
            };
            Assert.Throws<ArgumentException>( () => KeyVault.EncryptValuesToString( vals, "A passphrase" ) );
        }
    }
}
