using CKli;
using CK.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env.Analysis
{
    public abstract class XEnvAction : XTypedObject
    {
        protected XEnvAction( Initializer intializer, List<XEnvAction> collector )
            : base( intializer )
        {
            collector.Add( this );
            Number = collector.Count;
            if( Title == null ) Title = XElement.Name.LocalName;
        }

        public int Number { get; private set; }

        public string Title { get; protected set; }

        public abstract bool Run( IActivityMonitor m );

        public override string ToString() => $"{Number} - {Title}";
    }
}
