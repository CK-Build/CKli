using CK.Env;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CK.Core;
using System.Net.Http;

namespace CKli
{
    public class XSharedHttpClient : XTypedObject, IDisposable
    {
        HttpClient _shared;

        public XSharedHttpClient( Initializer initializer )
            : base( initializer )
        {
            initializer.Services.Add( this );
        }

        public HttpClient Shared => _shared ?? (_shared = new HttpClient());

        void IDisposable.Dispose()
        {
            if( _shared != null ) _shared.Dispose();
        }
    }
}
