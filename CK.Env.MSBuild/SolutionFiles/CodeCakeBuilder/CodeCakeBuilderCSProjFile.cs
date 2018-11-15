using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env.MSBuild
{
    public class CodeCakeBuilderCSProjFile : GitFolderXmlFile, IGitBranchPlugin
    {
        readonly CodeCakeBuilderFolder _f;
        readonly ISolutionSettings _settings;

        public CodeCakeBuilderCSProjFile( CodeCakeBuilderFolder f, ISolutionSettings s )
            : base( f.Folder, "CodeCakeBuilder/CodeCakeBuilder.csproj" )
        {
            _f = f;
            _settings = s;
        }

        public void ApplySettings( IActivityMonitor m )
        {
            if( !_f.EnsureDirectory( m ) ) return;
            if( Document == null ) Document = new XDocument( new XElement( "Project",
                                                                new XAttribute( "Sdk", "Microsoft.NET.Sdk" )  ));
            var p = Document.Root.EnsureElement( "PropertyGroup" );
            p.SetElementValue( "TargetFramework", "netcoreapp2.1" );
            p.SetElementValue( "OutputType", "Exe" );
            p.SetElementValue( "LangVersion", "7.2" );

            var i = Document.Root.EnsureElement( "ItemGroup" );
            EnsureProjectReference( m, i, "CK.Text", "7.1.1--0008-develop" );
            EnsureProjectReference( m, i, "NuGet.Credentials", "4.8.0" );
            EnsureProjectReference( m, i, "NuGet.Protocol", "4.8.0" );
            EnsureProjectReference( m, i, "NUnit.ConsoleRunner", "3.9.0" );
            EnsureProjectReference( m, i, "NUnit.Runners.Net4", "2.6.4" );
            EnsureProjectReference( m, i, "SimpleGitVersion.Cake", "0.36.1--0015-develop" );
            Save( m );
        }

        void EnsureProjectReference( IActivityMonitor m, XElement itemGroup, string packageId, string version )
        {
            var r = itemGroup.Elements( "PackageReference" )
                             .FirstOrDefault( b => (string)b.Attribute( "Include" ) == packageId );
            if( r == null )
            {
                itemGroup.Add( r = new XElement( "PackageReference",
                                            new XAttribute( "Include", packageId ) ) );
            }
            r.SetAttributeValue( "Version", version );
        }

    }
}
