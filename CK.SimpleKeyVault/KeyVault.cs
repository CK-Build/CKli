using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CK.SimpleKeyVault
{
    /// <summary>
    /// This is the same algorithm and configuration as https://github.com/appveyor/secure-file.
    /// </summary>
    public static class KeyVault
    {
        private static readonly byte[] _magicHeader = new byte[] { 0, 148, 41, 0, 247, 87, 1, 2 };

        /// <summary>
        /// This salt must be the same as the one of CodeCake.
        /// </summary>
        const string _salt = "{E4E66F59-CAF2-4C39-A7F8-46097B1C461B}";

        /// <summary>
        /// The current version is 2.
        /// </summary>
        public const int CurrentVersion = 2;

        /// <summary>
        /// Creates a new <see cref="SymmetricAlgorithm"/>.
        /// </summary>
        /// <param name="passPhrase">Secret to use. Must not be null, empty or white space.</param>
        /// <param name="salt">Specific salt to use (this default value comes from https://github.com/appveyor/secure-file implementation).</param>
        /// <returns>The symmetric algorithm.</returns>
        public static SymmetricAlgorithm CreateCryptoAlgorithm( string passPhrase, string salt = _salt )
        {
            if( String.IsNullOrWhiteSpace( passPhrase ) ) throw new ArgumentNullException( nameof( passPhrase ) );
            using( var pbkdf2 = new Rfc2898DeriveBytes( passPhrase, Encoding.UTF8.GetBytes( salt ), 10000 ) )
            {
                Rijndael alg = Rijndael.Create();
                alg.Key = pbkdf2.GetBytes( 32 );
                alg.IV = pbkdf2.GetBytes( 16 );
                return alg;
            }
        }

        /// <summary>
        /// Decrypts a list of key value pairs previously encrypted by <see cref="EncryptValuesToString"/>.
        /// Throws <see cref="InvalidDataException"/> on error (bad password or invalid format).
        /// A missing first line with the "-- Version: " will be considered the <see cref="CurrentVersion"/>. 
        /// </summary>
        /// <param name="crypted">The crypted string. Can be null or empty.</param>
        /// <param name="passPhrase">Secret to use. Must not be null, empty or white space.</param>
        /// <param name="salt">Specific salt to use (this default value comes from https://github.com/appveyor/secure-file implementation).</param>
        /// <returns>The list of key value pairs.</returns>
        public static Dictionary<string, string?> DecryptValues( string crypted, string passPhrase, string salt = _salt )
        {
            var keys = new HashSet<string>();
            var result = new Dictionary<string, string?>();
            if( String.IsNullOrWhiteSpace( crypted ) ) return result;
            if( String.IsNullOrWhiteSpace( passPhrase ) ) throw new ArgumentException( "Pass phrase must not be empty.", nameof( passPhrase ) );

            int version = CurrentVersion;
            Match? mVersion = null;
            string[] lines = crypted.Split( new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries );
            foreach( var l in lines )
            {
                if( l.StartsWith( "--" ) )
                {
                    if( mVersion == null )
                    {
                        mVersion = Regex.Match( l, @"--\s*Version\s*:\s*(\d+)", RegexOptions.CultureInvariant );
                        if( mVersion.Success )
                        {
                            if( !int.TryParse( mVersion.Groups[1].Value, out version ) )
                            {
                                throw new InvalidDataException( "Unable to read version from file header: " + l );
                            }
                            if( version < 1 || version > CurrentVersion )
                            {
                                throw new InvalidDataException( $"Invalid or unhandled version {version}. Must be between 1 and {CurrentVersion}." );
                            }
                        }
                    }
                    continue;
                }
                if( l.StartsWith( " > " ) )
                {
                    byte[] bytes = Convert.FromBase64String( l.Substring( 3 ) );
                    using( var mem = new MemoryStream( bytes ) )
                    using( var algo = CreateCryptoAlgorithm( passPhrase, salt ) )
                    using( var read = new CryptoStream( mem, algo.CreateDecryptor(), CryptoStreamMode.Read ) )
                    using( var r = new BinaryReader( read ) )
                    {
                        if( version > 1 )
                        {
                            if( !r.ReadBytes( _magicHeader.Length ).SequenceEqual( _magicHeader ) )
                            {
                                throw new InvalidDataException( $"Invalid crypted content." );
                            }
                        }
                        try
                        {
                            int count = r.ReadInt32();
                            for( int i = 0; i < count; ++i )
                            {
                                var k = r.ReadString();
                                var v = r.ReadBoolean() ? r.ReadString() : null;
                                if( keys.Contains( k ) )
                                {
                                    result[k] = v;
                                }
                            }
                            return result;
                        }
                        catch( Exception ex )
                        {
                            throw new InvalidDataException( $"Invalid crypted content.", ex );
                        }
                    }
                }
                keys.Add( l );
            }
            throw new InvalidDataException( "Unable to find crypted values section." );
        }

        /// <summary>
        /// Encrypts values in a text based format where the keys are easily readable.
        /// </summary>
        /// <param name="values">
        /// The key value pairs for which value must be encrypted.
        /// No key must not be null, empty or contain \\r or \\n or space characters.</param>
        /// <param name="passPhrase">Secret to use. Must not be null, empty or white space.</param>
        /// <param name="salt">Specific salt to use (this default value comes from https://github.com/appveyor/secure-file implementation).</param>
        /// <returns>The encrypted string.</returns>
        public static string EncryptValuesToString( IDictionary<string, string?> values, string passPhrase, string salt = _salt )
        {
            if( values == null ) throw new ArgumentNullException( nameof( values ) );
            using( var mem = new MemoryStream() )
            using( var algo = CreateCryptoAlgorithm( passPhrase, salt ) )
            using( var output = new CryptoStream( mem, algo.CreateEncryptor(), CryptoStreamMode.Write ) )
            using( var w = new BinaryWriter( output ) )
            {
                var b = new StringBuilder();
                b.Append( "-- Version: " ).Append( CurrentVersion ).AppendLine();
                b.AppendLine( "-- Keys below can be removed if needed." );
                w.Write( _magicHeader );
                w.Write( values.Count );
                foreach( var kv in values )
                {
                    if( String.IsNullOrEmpty( kv.Key )
                        || kv.Key.IndexOf( '\n' ) >= 0
                        || kv.Key.IndexOf( '\r' ) >= 0
                        || kv.Key.IndexOf( ' ' ) >= 0 )
                    {
                        throw new ArgumentException( $"Key must not be null, empty or contain \\r or \\n or space characters: {kv.Key}", nameof( values ) );
                    }
                    b.AppendLine( kv.Key );
                    w.Write( kv.Key );
                    if( kv.Value == null )
                    {
                        w.Write( false );
                    }
                    else
                    {
                        w.Write( true );
                        w.Write( kv.Value );
                    }
                }
                w.Flush();
                output.FlushFinalBlock();
                b.Append( " > " ).AppendLine( Convert.ToBase64String( mem.ToArray() ) );
                return b.ToString();
            }
        }

        /// <summary>
        /// Encrypts a file into another file (that must not exist) with a given secret.
        /// </summary>
        /// <param name="fileName">File to encrypt.</param>
        /// <param name="outFileName">Target file that will be encrypted. Must not exist.</param>
        /// <param name="passPhrase">Secret to use. Must not be null, empty or white space.</param>
        /// <param name="salt">Specific salt to use (this default value comes from https://github.com/appveyor/secure-file implementation).</param>
        public static void Encrypt( string fileName, string outFileName, string passPhrase, string salt = _salt )
        {
            if( fileName == null ) throw new ArgumentNullException( nameof( fileName ) );
            if( outFileName == null ) throw new ArgumentNullException( nameof( outFileName ) );

            using( var algo = CreateCryptoAlgorithm( passPhrase, salt ) )
            using( var inStream = File.OpenRead( fileName ) )
            {
                using( var outStream = File.Create( outFileName ) )
                {
                    using( var cryptoStream = new CryptoStream( outStream, algo.CreateEncryptor(), CryptoStreamMode.Write ) )
                    {
                        inStream.CopyTo( cryptoStream );
                    }
                }
            }
        }

        /// <summary>
        /// Decrypts a file into another file with a given secret.
        /// </summary>
        /// <param name="fileName">File to decrypt.</param>
        /// <param name="outFileName">Target file that will be decrypted.</param>
        /// <param name="passPhrase">Secret to use. Must not be null, empty or white space.</param>
        /// <param name="salt">Specific salt to use (this default value comes from https://github.com/appveyor/secure-file implementation).</param>
        public static void Decrypt( string fileName, string outFileName, string passPhrase, string salt = _salt )
        {
            if( fileName == null ) throw new ArgumentNullException( nameof( fileName ) );
            if( outFileName == null ) throw new ArgumentNullException( nameof( outFileName ) );

            using( var algo = CreateCryptoAlgorithm( passPhrase, salt ) )
            using( var inStream = File.OpenRead( fileName ) )
            {
                using( var outStream = File.Create( outFileName ) )
                {
                    using( var cryptoStream = new CryptoStream( outStream, algo.CreateDecryptor(), CryptoStreamMode.Write ) )
                    {
                        inStream.CopyTo( cryptoStream );
                    }
                }
            }
        }

    }
}
