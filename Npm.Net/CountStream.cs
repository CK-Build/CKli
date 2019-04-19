using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Npm.Net
{
    /// <summary>
    /// Allow 
    /// </summary>
    public class CountStream : Stream
    {
        readonly Stream _stream;
        long _position;
        /// <summary>
        /// Allow to Get the current position when the SubStream does not allow it.
        /// It simply count everything you read/write to know the current Position
        /// </summary>
        /// <param name="stream">Base stream</param>
        /// <param name="ignoreCanSeek">There is no point to use this class if you CanSeek (so you can read the position) so i throw an Exception. Set this to <see langword="true"/> to ignore this and it won't throw an <see cref="ArgumentException"/> <</param>
        public CountStream( Stream stream, bool ignoreCanSeek = false )
        {
            if( !ignoreCanSeek && stream.CanSeek ) throw new ArgumentException( "No point to calculate the position when you can directly get it" );
            _stream = stream;
        }

        public override bool CanRead => _stream.CanRead;

        public override bool CanSeek => _stream.CanSeek;

        public override bool CanWrite => _stream.CanWrite;

        public override long Length => _stream.Length;

        public override long Position { get => _position; set => _stream.Position = value; }//the setter should'nt work, but it won't hurt if let the original stream throw.

        public override void Flush() => _stream.Flush();

        public override int Read( byte[] buffer, int offset, int count )
        {
            int amount = _stream.Read( buffer, offset, count );
            Interlocked.Add( ref _position, amount );
            return amount;
        }

        public override int Read( Span<byte> buffer )
        {
            int amount = _stream.Read( buffer );
            Interlocked.Add( ref _position, amount );
            return amount;
        }

        public override async Task<int> ReadAsync( byte[] buffer, int offset, int count, CancellationToken cancellationToken )
        {
            int amount = await base.ReadAsync( buffer, offset, count, cancellationToken );
            Interlocked.Add( ref _position, amount );
            return amount;
        }
        public override async ValueTask<int> ReadAsync( Memory<byte> buffer, CancellationToken cancellationToken = default )
        {
            int amount = await base.ReadAsync( buffer, cancellationToken );
            Interlocked.Add( ref _position, amount );
            return amount;
        }

        public override int ReadByte()
        {
            int amount = base.ReadByte();
            Interlocked.Add( ref _position, amount );
            return amount;
        }

        public override long Seek( long offset, SeekOrigin origin )
        {
            throw new NotSupportedException();
        }

        public override void SetLength( long value )
        {
            throw new NotSupportedException();
        }

        public override void Write( byte[] buffer, int offset, int count )
        {
            Interlocked.Add( ref _position, count );
            _stream.Write( buffer, offset, count );
        }

        public override void Write( ReadOnlySpan<byte> buffer )
        {
            Interlocked.Add( ref _position, buffer.Length );
            _stream.Write( buffer );
        }
        public override Task WriteAsync( byte[] buffer, int offset, int count, CancellationToken cancellationToken )
        {
            Interlocked.Add( ref _position, count );
            return _stream.WriteAsync( buffer, offset, count, cancellationToken );
        }
        public override ValueTask WriteAsync( ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default )
        {
            Interlocked.Add( ref _position, buffer.Length );
            return base.WriteAsync( buffer, cancellationToken );
        }

        public override void WriteByte( byte value )
        {
            Interlocked.Increment( ref _position );
            base.WriteByte( value );
        }
    }
}
