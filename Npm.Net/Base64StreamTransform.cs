using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Npm.Net
{
    class Base64StreamLength : Stream
    {
        readonly Stream _stream;
        long _baseStreamLength;
        public Base64StreamLength( Stream stream)
        {
            _baseStreamLength = stream.Length;
            _stream =  new CryptoStream( stream, new ToBase64Transform(), CryptoStreamMode.Read );
        }

        public override bool CanRead => _stream.CanRead;

        public override bool CanSeek => _stream.CanSeek;

        public override bool CanWrite => _stream.CanWrite;

        public override long Length => ((4 * _baseStreamLength / 3) + 3) & ~3;

        public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public override void Flush() => _stream.Flush();

        public override int Read( byte[] buffer, int offset, int count )
        {
            return _stream.Read( buffer, offset, count );
        }

        public override long Seek( long offset, SeekOrigin origin )
        {
            return _stream.Seek( offset, origin );
        }

        public override void SetLength( long value )
        {
            _stream.SetLength( value );
        }

        public override void Write( byte[] buffer, int offset, int count )
        {
            _stream.Write( buffer, offset, count );
        }
    }
}
