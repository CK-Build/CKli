using CK.Core;
using System.Diagnostics;
using System.Diagnostics.Contracts;

namespace CK.Build
{
    /// <summary>
    /// Internal struct.
    /// </summary>
    readonly ref struct SerializerContext
    {
        public const int Version = 0;

        public readonly ICKBinaryWriter Writer;
        public readonly CKBinaryWriter.ObjectPool<CKTraitContext> TraitContextPool;
        public readonly CKBinaryWriter.ObjectPool<CKTrait?> TraitPool;

        public SerializerContext( ICKBinaryWriter writer )
        {
            (Writer = writer).WriteNonNegativeSmallInt32( Version );
            TraitContextPool = new CKBinaryWriter.ObjectPool<CKTraitContext>( Writer, PureObjectRefEqualityComparer<CKTraitContext>.Default );
            TraitPool = new CKBinaryWriter.ObjectPool<CKTrait?>( Writer, PureObjectRefEqualityComparer<CKTrait?>.Default );
        }

        public void Write( CKTrait? t )
        {
            if( TraitPool.MustWrite( t ) )
            {
                Debug.Assert( t != null, "If it must be written, then it is not null." );
                if( TraitContextPool.MustWrite( t.Context ) )
                {
                    t.Context.Write( Writer );
                }
                Writer.WriteSharedString( t.ToString() );
            }
        }

        public void WriteKnownContextTrait( CKTrait? t ) => Writer.WriteSharedString( t?.ToString() );

    }
}
