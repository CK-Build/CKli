using CK.Core;
using CK.Env;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CKli
{
    public class XPublishKeyStore : XTypedObject, IPublishKeyStore
    {
        string _myGetApiKey;
        string _remoteStorePushApiKey;

        public XPublishKeyStore( Initializer initializer )
            : base( initializer )
        {
            initializer.Services.Add( this );
        }

        public string GetCKSetupRemoteStorePushKey( IActivityMonitor m )
        {
            if( _remoteStorePushApiKey == null )
            {
                Console.Write( "Enter https://cksetup.invenietis.net/ key to push components: " );
                _remoteStorePushApiKey = Console.ReadLine();
                if( String.IsNullOrEmpty( _remoteStorePushApiKey ) ) _remoteStorePushApiKey = null;
            }
            return _remoteStorePushApiKey;
        }

        public string GetMyGetPushKey( IActivityMonitor m )
        {
            if( _myGetApiKey == null )
            {
                Console.Write( "Enter MYGET_API_KEY to push packages to remote feed: " );
                _myGetApiKey = Console.ReadLine();
                if( String.IsNullOrEmpty( _myGetApiKey ) ) _myGetApiKey = null;
            }
            return _myGetApiKey;
        }

    }
}
