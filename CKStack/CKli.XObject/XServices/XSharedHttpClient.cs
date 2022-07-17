using CK.Core;
using CK.Env;
using System;
using System.Net.Http;

namespace CKli
{
    public class XSharedHttpClient : XTypedObject, IDisposable
    {
        readonly HttpClient _shared;

        public XSharedHttpClient( Initializer initializer )
            : base( initializer )
        {
            _shared = new HttpClient();
            initializer.Services.Add( _shared );
        }

        public HttpClient Shared => _shared;

        void IDisposable.Dispose() => _shared.Dispose();
    }
}
