using CK.Core;
using CK.Env.Analysis;
using System;
using System.Collections.Generic;
using System.Text;

namespace CKli
{
    public class XReadDependency : XEnvAction
    {
        public XReadDependency( Initializer intializer, List<XEnvAction> collector )
            : base( intializer, collector )
        {
        }

        public override bool Run( IActivityMonitor m )
        {
            throw new NotImplementedException();
        }
    }
}
