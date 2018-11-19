using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env.MSBuild.SolutionFiles
{
    public class CodeCakeBuilderCSProjFile : GitFolderXmlFile, IGitBranchPlugin, ICommandMethodsProvider
    {
        readonly CodeCakeBuilderFolder _f;
        readonly ISolutionSettings _settings;

        public CodeCakeBuilderCSProjFile( CodeCakeBuilderFolder f, ISolutionSettings settings, NormalizedPath branchPath )
            : base( f.Folder, f.FolderPath.AppendPart( "CodeCakeBuilder.csproj" ) )
        {
            _f = f;
            _settings = settings;
            BranchPath = branchPath;
        }

        public NormalizedPath BranchPath { get; }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FilePath;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( !_f.EnsureDirectory( m ) ) return;
            if( Document == null ) Document = new XDocument( new XElement( "Project",
                                                                new XAttribute( "Sdk", "Microsoft.NET.Sdk" )  ));
            var p = Document.Root.EnsureElement( "PropertyGroup" );
            p.SetElementValue( "TargetFramework", "netcoreapp2.1" );
            p.SetElementValue( "OutputType", "Exe" );
            p.SetElementValue( "LangVersion", "7.2" );

            EnsureProjectReference( m, "CK.Text", "7.1.1--0008-develop" );
            EnsureProjectReference( m, "NuGet.Credentials", "4.8.0" );
            EnsureProjectReference( m, "NuGet.Protocol", "4.8.0" );
            if( !_settings.NoUnitTests )
            {
                EnsureProjectReference( m, "NUnit.ConsoleRunner", "3.9.0" );
                EnsureProjectReference( m, "NUnit.Runners.Net4", "2.6.4" );
            }
            EnsureProjectReference( m, "SimpleGitVersion.Cake", "0.36.1--0015-develop" );
            if( _settings.ProduceCKSetupComponents )
            {
                EnsureProjectReference( m, "CKSetup.Cake", "0.36.1--0015-develop" );
            }
            Save( m );
        }

        void EnsureProjectReference( IActivityMonitor m, string packageId, string version )
        {
            if( !IsProjectReference( m, packageId ) )
            {
                var r = Document.Root.Elements( "ItemGroup" )
                                     .Elements( "PackageReference" )
                                     .FirstOrDefault( b => (string)b.Attribute( "Include" ) == packageId );
                if( r == null )
                {
                    var itemGroup = Document.Root.Elements( "ItemGroup" ).FirstOrDefault( g => g.Element( "PackageReference" ) != null )
                                    ?? Document.Root.EnsureElement( "ItemGroup" );
                    itemGroup.Add( r = new XElement( "PackageReference",
                                                new XAttribute( "Include", packageId ) ) );
                }
                r.SetAttributeValue( "Version", version );
            }
        }

        bool IsProjectReference( IActivityMonitor m, string packageId )
        {
            var p = packageId + ".csproj";
            return Document.Root.Elements( "ItemGroup" )
                                .Elements( "ProjectReference" )
                                .Attributes( "Include" )
                                .Any( a => new NormalizedPath( a.Value ).LastPart == p );
        }
    }
}
