using CK.Core;
using CK.Env;
using CK.Env.MSBuild;
using CK.NuGetClient;
using CK.Text;
using SimpleGitVersion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CKli
{
    public class XSolutionSettings : XTypedObject
    {
        public XSolutionSettings(
            Initializer initializer,
            XSolutionSettings previous = null )
            : base( initializer )
        {
            if( previous != null )
            {
                SolutionSettings = new SolutionSettings( previous.SolutionSettings, initializer.Element );
            }
            else SolutionSettings = new SolutionSettings( initializer.Element );
            initializer.Services.Add( this );
        }

        public ISolutionSettings SolutionSettings { get; }

    }
}
