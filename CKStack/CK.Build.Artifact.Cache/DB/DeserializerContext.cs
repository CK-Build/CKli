using CK.Core;

namespace CK.Build.PackageDB
{
    /// <summary>
    /// Internal struct.
    /// </summary>
    readonly ref struct DeserializerContext
    {
        public readonly int Version;
        public readonly ICKBinaryReader Reader;
        public readonly CKBinaryReader.ObjectPool<CKTraitContext> TraitContextPool;
        public readonly CKBinaryReader.ObjectPool<CKTrait> TraitPool;

        public DeserializerContext( ICKBinaryReader reader )
        {
            Version = reader.ReadNonNegativeSmallInt32();
            Reader = reader;
            TraitContextPool = new CKBinaryReader.ObjectPool<CKTraitContext>( Reader );
            TraitPool = new CKBinaryReader.ObjectPool<CKTrait>( Reader );
        }

        public CKTrait ReadCKTrait()
        {
            var stateT = TraitPool.TryRead( out var t );
            if( !stateT.Success )
            {
                var stateC = TraitContextPool.TryRead( out var ctx );
                if( !stateC.Success )
                {
                    stateC.SetReadResult( ctx = CKTraitContext.Read( Reader ) );
                }
                stateT.SetReadResult( t = ctx.FindOrCreate( Reader.ReadSharedString() ) );
            }
            return t;
        }

        public CKTrait? ReadKnownContextTrait( CKTraitContext ctx )
        {
            var t = Reader.ReadSharedString();
            return t != null ? ctx.FindOrCreate( t ) : null;
        }
    }
}
