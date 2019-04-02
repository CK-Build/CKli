using CK.Core;
using CK.Env;
using CK.Env.NPM;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CKli
{
    public class XNPMProject : XTypedObject, INPMProjectSpec
    {
        public XNPMProject(
                Initializer initializer,
                XNPMProjectCenter projectCenter,
                XPrimarySolution solution
            )
            : base( initializer )
        {
            Solution = solution;
            if( PackageName == null ) PackageName = Folder.LastPart.ToLowerInvariant();
            Solution.NPMProjects.Add( this );
        }

        public XPrimarySolution Solution { get; }

        public NormalizedPath Folder { get; private set; }

        public string PackageName { get; private set; }

        public bool IsPrivate { get; private set; }

        public NormalizedPath FullPath => Solution.FullPath.Combine( Folder );

    }
}
