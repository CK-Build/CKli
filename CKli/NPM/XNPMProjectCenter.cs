using CK.Core;
using CK.Env;
using CK.Env.NPM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CKli
{
    public class XNPMProjectCenter : XTypedObject
    {
        public XNPMProjectCenter(
            Initializer initializer,
            FileSystem fs )
            : base( initializer )
        {
            initializer.Services.Add( this );
            ProjectContext = new NPMProjectContext( fs );
        }

        public NPMProjectContext ProjectContext { get; }

    }
}
