using CK.Core;
using CK.Text;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CK.Env
{
    public class TransformedFileProvider : IFileProvider
    {
        readonly IFileProvider _f;
        readonly Func<string, IFileInfo, IFileInfo> _trans;

        public TransformedFileProvider( IFileProvider f, Func<string,IFileInfo,IFileInfo> trans )
        {
            if( f == null ) throw new ArgumentNullException( nameof( f ) );
            if( trans == null ) throw new ArgumentNullException( nameof( trans ) );
            _f = f;
            _trans = trans;
        }

        class Content : IDirectoryContents
        {
            readonly Func<string,IFileInfo, IFileInfo> _t;
            readonly IDirectoryContents _inner;
            readonly NormalizedPath _path;

            public Content( NormalizedPath p, Func<string,IFileInfo, IFileInfo> t, IDirectoryContents inner )
            {
                _path = p;
                _t = t;
                _inner = inner;
            }

            public bool Exists => _inner.Exists;

            public IEnumerator<IFileInfo> GetEnumerator() => _inner.Select( f => _t( _path.Path + Path.DirectorySeparatorChar + f.Name, f ) ).GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public IDirectoryContents GetDirectoryContents( string subPath )
        {
            return new Content( subPath, _trans, _f.GetDirectoryContents( subPath ) );
        }

        public IFileInfo GetFileInfo( string subpath )
        {
            return _trans( subpath, _f.GetFileInfo( subpath ) );
        }

        public IChangeToken Watch( string filter )
        {
            throw new NotSupportedException();
        }
    }
}
