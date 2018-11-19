using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env.MSBuild.SolutionFiles
{
    public class CodeCakeBuilderFolder : PluginFolderBase
    {
        readonly ISolutionSettings _settings;

        public CodeCakeBuilderFolder( GitFolder f, ISolutionSettings settings, NormalizedPath branchPath )
            : base( f, branchPath, "CodecakeBuilder" )
        {
            _settings = settings;
        }

        protected override void DoApplySettings( IActivityMonitor m )
        {
            CopyTextResource( m, "InstallCredentialProvider.ps1" );
            CopyTextResource( m, "Program.cs" );
            CopyTextResource( m, "Build.cs", AdaptBuild );
            CopyTextResource( m, "Build.NuGetHelper.cs" );
            CopyTextResource( m, "Build.StandardCheckRepository.cs" );
            CopyTextResource( m, "Build.StandardSolutionBuild.cs" );
            if( _settings.NoUnitTests )
            {
                DeleteFile( m, "Build.StandardUnitTests.cs" );
            }
            else
            {
                CopyTextResource( m, "Build.StandardUnitTests.cs" );
            }
            CopyTextResource( m, "Build.StandardCreateNuGetPackages.cs" );
            CopyTextResource( m, "Build.StandardPushNuGetPackages.cs" );
        }

        string AdaptBuild( string text )
        {
            var name = Folder.SubPath.LastPart;
            Regex r = new Regex(
                  "(?<1>const\\s+string\\s+solutionName\\s*=\\s*\").*?(?<2>\";\\s*//\\s*!Transformable)",
                  RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant );
            return r.Replace( text, "$1"+name+"$2" );
        }
    }
}
