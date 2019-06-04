using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Npm.Net
{
    /// <summary>
    /// Allow multiple stream to be read sequentialy
    /// </summary>
    class MergeStream : Stream, IDisposable
    {
        List<Stream> Streams { get; set; }
        int _streamIndex;
        Stream CurrentStream
        {
            get
            {
                if( _streamIndex >= Streams.Count ) return null;
                return Streams[_streamIndex];
            }
        }

        public event EventHandler<Stream> OnNextStream;

        public override bool CanRead => true;

        public override bool CanSeek => Streams.All( p => p.CanSeek );

        public override bool CanWrite => false;

        public override long Length => Streams.Sum( p => p.Length );

        public override long Position
        {
            get
            {
                long sum = 0;
                for( int i = 0; i < _streamIndex; i++ )
                {
                    sum += Streams[0].Length;
                }
                return sum + CurrentStream.Position;
            }
            set
            {
                Stream oldStream = CurrentStream;
                int index = 0;
                foreach( Stream Stream in Streams )
                {
                    if( Stream.Length < value )
                    {
                        Stream.Position = value;
                        _streamIndex = index;
                        if( CurrentStream != oldStream ) OnNextStream( this, CurrentStream );
                        return;
                    }
                    value -= Stream.Length;
                    index++;
                }
                throw new IndexOutOfRangeException();
            }
        }
        public MergeStream()
        {
            Streams = new List<Stream>();
        }

        public void AddStream( Stream stream )
        {
            if( !stream.CanRead ) throw new ArgumentException( "stream.CanRead is false" );
            Streams.Add( stream );
        }

        public override void Flush()
        {
            throw new NotSupportedException();
        }

        public override int Read( byte[] buffer, int offset, int count )
        {
            if( offset + count > buffer.Length ) throw new ArgumentException( "offset+count>buffer.Length" );
            byte[] oldBuffer = buffer;
            int result = CurrentStream.Read( buffer, offset, count );
            if( result != 0 ) return result;
            if( oldBuffer != buffer ) throw new InvalidOperationException( "Stream returned 0 but modified the buffer !" );
            _streamIndex++;
            OnNextStream?.Invoke( this, CurrentStream );
            if( _streamIndex == Streams.Count ) return 0;
            if( CanSeek ) CurrentStream.Position = 0;
            return Read( buffer, offset, count );
        }

        public override async Task<int> ReadAsync( byte[] buffer, int offset, int count, CancellationToken cancellationToken )
        {
            if( offset + count > buffer.Length ) throw new ArgumentException( "offset+count>buffer.Length" );
            byte[] oldBuffer = buffer;
            int result = await CurrentStream.ReadAsync( buffer, offset, count );
            if( result != 0 ) return result;
            if( oldBuffer != buffer ) throw new InvalidOperationException( "Stream returned 0 but modified the buffer !" );
            _streamIndex++;
            OnNextStream?.Invoke( this, CurrentStream );
            if( _streamIndex == Streams.Count ) return 0;
            if( CanSeek ) CurrentStream.Position = 0;
            return await ReadAsync( buffer, offset, count );
        }

        //public override async ValueTask<int> ReadAsync( Memory<byte> buffer, CancellationToken cancellationToken = default )
        //{
        //    Memory<byte> oldBuffer = buffer;
        //    int result = await CurrentStream.ReadAsync( buffer, cancellationToken );
        //    if( result != 0 ) return result;
        //    if( !oldBuffer.Equals( buffer) ) throw new InvalidOperationException( "Stream returned 0 but modified the buffer !" );//I think it's bugged.
        //    _streamIndex++;
        //    OnNextStream?.Invoke( this, CurrentStream );
        //    if( _streamIndex == Streams.Count ) return 0;
        //    if( CanSeek ) CurrentStream.Position = 0;
        //    return await ReadAsync( buffer, cancellationToken );
        //}

        public override long Seek( long offset, SeekOrigin origin )
        {
            switch( origin )
            {
                case SeekOrigin.Begin:
                    Position = offset;
                    break;
                case SeekOrigin.Current:
                    Position += offset;
                    break;
                case SeekOrigin.End:
                    Position = Length + offset;
                    break;
                default:
                    throw new NotSupportedException();
            }
            return Position;
        }

        public override void SetLength( long value )
        {
            throw new NotSupportedException();
        }

        public override void Write( byte[] buffer, int offset, int count )
        {
            throw new NotSupportedException();
        }
        protected override void Dispose( bool disposing )
        {
            foreach( Stream stream in Streams )
            {
                stream.Dispose();
            }
            base.Dispose( disposing );
        }
    }
}
