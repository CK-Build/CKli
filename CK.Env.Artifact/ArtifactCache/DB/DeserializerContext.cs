using CK.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    /// <summary>
    /// Internal struct. The ref is just for fun (a simple readonly would be enough).
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

        public CKTrait ReadExistingTrait( CKTraitContext ctx ) => ctx.FindOrCreate( Reader.ReadSharedString() );

    }
}