using CK.Core;
using CK.Env.Analysis;
using System;
using System.Collections.Generic;
using System.Text;

namespace CKli
{
    public class XReadDependencyAction : XAction
    {
        public XReadDependencyAction( Initializer intializer, ActionCollector collector )
            : base( intializer, collector )
        {
        }

        public override bool Run( IActivityMonitor m )
        {
            throw new NotImplementedException();
        }
    }
}
