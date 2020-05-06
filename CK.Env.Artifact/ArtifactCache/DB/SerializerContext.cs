using CK.Core;
using System.Diagnostics;

namespace CK.Env
{
    /// <summary>
    /// Internal struct. The ref is just for fun (a simple readonly would be enough).
    /// </summary>
    readonly ref struct SerializerContext
    {
        public readonly ICKBinaryWriter Writer;
        public readonly CKBinaryWriter.ObjectPool<CKTraitContext> TraitContextPool;
        public readonly CKBinaryWriter.ObjectPool<CKTrait?> TraitPool;

        public SerializerContext( ICKBinaryWriter writer, int version )
        {
            (Writer = writer).WriteNonNegativeSmallInt32( version );
            TraitContextPool = new CKBinaryWriter.ObjectPool<CKTraitContext>( Writer, PureObjectRefEqualityComparer<CKTraitContext>.Default );
            TraitPool = new CKBinaryWriter.ObjectPool<CKTrait?>( Writer, PureObjectRefEqualityComparer<CKTrait?>.Default );
        }

        public void Write( CKTrait? t )
        {
            if( TraitPool.MustWrite( t ) )
            {
                Debug.Assert( t != null, "If it lust be written, then it is not null." );
                if( TraitContextPool.MustWrite( t.Context ) )
                {
                    t.Context.Write( Writer );
                }
                Writer.WriteSharedString( t.ToString() );
            }
        }

        public void WriteExistingTrait( CKTrait t ) => Writer.WriteSharedString( t.ToString() );

    }
}
