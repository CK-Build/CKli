using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using CK.Core;
using CK.Env.DependencyModel;
using CK.Env.MSBuildSln;
using CK.Text;
using Microsoft.Extensions.FileProviders;

namespace CK.Env.Plugin
{
    public class CSProjFile : GitBranchPluginBase, ICommandMethodsProvider
    {
        private readonly SolutionSpec _solutionSpec;
        private readonly SolutionDriver _solutionDriver;

        public CSProjFile(
             GitFolder f,
             NormalizedPath branchPath,
            SolutionSpec solutionSpec,
            SolutionDriver solutionDriver )
             : base( f, branchPath )
        {
            _solutionSpec = solutionSpec;
            _solutionDriver = solutionDriver;
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => _solutionDriver.BranchPath.AppendPart( nameof( CSProjFile ) );

        [CommandMethod]
        public bool ApplySettings( IActivityMonitor m )
        {
            if( !this.CheckCurrentBranch( m ) ) return false;
            var sln = _solutionDriver.GetSolution( m );
            var csprojs = sln.Projects.Select<IProject, (IProject project, MSProject msproject)>( p => (p, p.Tag<MSProject>()) ).Where( p => p.Item2 != null );
            foreach( var p in csprojs )
            {

                if( p.project.IsPublished )
                {
                    //For test projects we want the IsPackable element to be explicit.
                    EnsurePack( m, p.project.IsTestProject ? true : (bool?)null, p.msproject.Path );
                }
                else
                {
                    EnsurePack( m, false, p.msproject.Path );
                }
            }
            return true;
        }

        void EnsurePack( IActivityMonitor m, bool? packTarget, string path )
        {
            IFileInfo info = GitFolder.FileSystem.GetFileInfo( path );

            XDocument csproj = XDocument.Parse( info.ReadAsText() );
            var properties = csproj.Root.Elements( "PropertyGroup" );
            List<XElement> isPackableElements = properties.Elements( "IsPackable" ).ToList();
            isPackableElements.Reverse();
            if( isPackableElements.Count() > 1 )
            {
                m.Info( $"Removing duplicate IsPackable in {path} " );
                isPackableElements.Skip( 1 ).Remove();
            }
            var property = properties.First();
            if( isPackableElements.Count() == 0 )
            {
                if( packTarget == null )
                {
                    File.WriteAllText( info.PhysicalPath, csproj.ToString() );
                    return;
                }
                property.Add( new XElement( "IsPackable", packTarget ) );
                File.WriteAllText( info.PhysicalPath, csproj.ToString() );
                return;
            }
            var packableElement = isPackableElements.First();
            if( packTarget == null)
            {
                if( (bool?)packableElement != true )
                {
                    packableElement.Remove();
                }
                else
                {
                    packableElement.Value = "True";
                }
            }
            else
            {
                packableElement.Value = packTarget.ToString();
            }
            File.WriteAllText( info.PhysicalPath, csproj.ToString() );
        }
    }
}
