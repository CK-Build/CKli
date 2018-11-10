using CK.Core;
using CK.Env;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CKli
{
    public class XSecretKeyStore : XTypedObject, ISecretKeyStore, CK.NuGetClient.ISecretKeyStore
    {
        readonly Dictionary<string, string> _keys;

        public XSecretKeyStore( Initializer initializer )
            : base( initializer )
        {
            _keys = new Dictionary<string, string>();
            initializer.Services.Add( this );
        }

        public string GetSecretKey( IActivityMonitor m, string name, bool throwOnEmpty, string message = null )
        {
            if( _keys.TryGetValue( name, out var value )
                || (value == null && throwOnEmpty))
            {
                if( message != null ) Console.WriteLine( message );
                if( throwOnEmpty ) Console.WriteLine( "!Required!" );
                Console.Write( $"Enter {name}: " );
                var v = Console.ReadLine();
                value = String.IsNullOrEmpty( v ) ? null : v;
                if( value == null && throwOnEmpty )
                {
                    throw new Exception( $"Secret '{name}' is required. {message}" );
                }
                _keys.Add( name, value );
            }
            return value;
        }

    }
}
