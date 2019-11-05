using CK.Core;
using CK.Env.MSBuildSln;
using CK.Text;
using System.Linq;
using System.Text;

namespace CK.Env.Plugin
{
    public class CodeCakeBuilderCSProjFile : XmlFilePluginBase, ICommandMethodsProvider
    {
        readonly CodeCakeBuilderFolder _f;
        readonly SolutionDriver _solutionDriver;

        public CodeCakeBuilderCSProjFile( CodeCakeBuilderFolder f, NormalizedPath branchPath, SolutionDriver solutionDriver )
            : base( f.GitFolder, branchPath, f.FolderPath.AppendPart( "CodeCakeBuilder.csproj" ), Encoding.UTF8 )
        {
            _f = f;
            _solutionDriver = solutionDriver;
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FilePath;

        public bool CanApplySettings => GitFolder.CurrentBranchName == BranchPath.LastPart;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( !_f.EnsureDirectory( m ) ) return;
            var solution = _solutionDriver.GetSolution( m, allowInvalidSolution: true );
            if( solution == null ) return;

            var slnFile = solution.Tag<SolutionFile>();
            MSProject ccbProject = slnFile.MSProjects.SingleOrDefault( p => p.ProjectName == "CodeCakeBuilder" );
            if( ccbProject == null )
            {
                m.Error( $"Missing CodeCakeBuilder project in '{slnFile.FilePath}'." );
                return;
            }
            //slnFile.Save( m );//TODO
        }
    }
}
