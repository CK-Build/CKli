using CK.Core;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CK.Env
{
    public class TransformedFileProvider : IFileProvider
    {
        readonly IFileProvider _f;
        readonly Func<IFileInfo, IFileInfo> _trans;

        public TransformedFileProvider( IFileProvider f, Func<IFileInfo,IFileInfo> trans )
        {
            if( f == null ) throw new ArgumentNullException( nameof( f ) );
            if( trans == null ) throw new ArgumentNullException( nameof( trans ) );
            _f = f;
            _trans = trans;
        }

        class Content : IDirectoryContents
        {
            readonly Func<IFileInfo, IFileInfo> _t;
            readonly IDirectoryContents _inner;

            public Content( Func<IFileInfo, IFileInfo> t, IDirectoryContents inner )
            {
                _t = t;
                _inner = inner;
            }

            public bool Exists => _inner.Exists;

            public IEnumerator<IFileInfo> GetEnumerator() => _inner.Select( f => _t( f ) ).GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public IDirectoryContents GetDirectoryContents( string subpath )
        {
            return new Content( _trans, _f.GetDirectoryContents( subpath ) );
        }

        public IFileInfo GetFileInfo( string subpath )
        {
            return _trans( _f.GetFileInfo( subpath ) );
        }

        public IChangeToken Watch( string filter )
        {
            throw new NotSupportedException();
        }
    }
}
