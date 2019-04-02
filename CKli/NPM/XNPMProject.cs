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
    public class XNPMProject : XTypedObject, INPMProjectDescription
    {
        public XNPMProject(
                Initializer initializer
            )
            : base( initializer )
        {
            if( !(initializer.Parent is XNPMProjects projects) ) throw new Exception( "A NPMProject must be a direct child of a NPMProjects." );
            Projects = projects;
            if( PackageName == null ) PackageName = Folder.LastPart.ToLowerInvariant();
        }

        public XNPMProjects Projects { get; }

        public string PackageName { get; private set; }

        public bool IsPrivate { get; private set; }

        public NormalizedPath Folder { get; private set; }

        public NormalizedPath FullPath => Projects.Solution.FullPath.Combine( Folder );

    }
}
