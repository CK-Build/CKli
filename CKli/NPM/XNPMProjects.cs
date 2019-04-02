using CK.Core;
using CK.Env;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CKli
{
    public class XNPMProjects : XTypedObject
    {
        public XNPMProjects(
                Initializer initializer,
                XPrimarySolution forbidden = null
            )
            : base( initializer )
        {
            if( !(initializer.Parent is XBranch) ) throw new Exception( "XNPMProjects must be a direct child of a Git branch." );
            if( forbidden != null ) throw new Exception( "NPMProjects must be defined before the PrimarySolution." );
            initializer.Services.Add( this );
        }

        public IReadOnlyList<XNPMProject> Projects { get; private set; }

        public XPrimarySolution Solution { get; private set; }

        protected override bool OnCreated( Initializer initializer )
        {
            Projects = Children.OfType<XNPMProject>().ToArray();
            return base.OnCreated( initializer );
        }

        internal void SetSolution( XPrimarySolution s )
        {
            Solution = s;
        }
    }
}
