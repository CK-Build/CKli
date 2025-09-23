using CK.Core;
using CSemVer;
using System;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CKli.Core;

sealed partial class PluginMachinery
{
    sealed class CKliPluginsProject
    {
        readonly PluginMachinery _machinery;
        readonly XDocument _csProj;
        readonly XElement _csFirstItemGroup;
        string _ckliPluginsFileText;
        int _autoSectionEnd;
        int _linePrefixLength;

        readonly XDocument _directoryPackages;
        readonly XElement _dpFirstItemGroup;

        CKliPluginsProject( PluginMachinery machinery )
        {
            _machinery = machinery;
            _csProj = XDocument.Load( machinery.CKliPluginsCSProj );
            Throw.CheckData( _csProj.Root != null );
            _csFirstItemGroup = _csProj.Root.Elements( "ItemGroup" ).FirstOrDefault()!;
            Throw.CheckData( _csFirstItemGroup != null );
            _ckliPluginsFileText = File.ReadAllText( machinery.CKliPluginsFile );
            _autoSectionEnd = _ckliPluginsFileText.IndexOf( "// </AutoSection>" );
            Throw.CheckData( _autoSectionEnd > 20 );
            _linePrefixLength = _autoSectionEnd - _ckliPluginsFileText.LastIndexOf( '\n', _autoSectionEnd );
            Throw.CheckData( _linePrefixLength >= 0 );
            _directoryPackages = XDocument.Load( machinery.DirectoryPackageProps );
            Throw.CheckData( _directoryPackages.Root != null );
            _dpFirstItemGroup = _directoryPackages.Root.Elements( "ItemGroup" ).FirstOrDefault()!;
            Throw.CheckData( _dpFirstItemGroup != null );
        }

        public static CKliPluginsProject? Create( IActivityMonitor monitor, PluginMachinery machinery )
        {
            try
            {
                return new CKliPluginsProject( machinery );
            }
            catch( Exception ex )
            {
                monitor.Error( "While loading CKli.Plugins project.", ex );
                return null;
            }
        }

        public bool RemovePlugin( IActivityMonitor monitor,
                                  string shortPluginName,
                                  string fullPluginName,
                                  out bool wasProjectReference )
        {
            wasProjectReference = false;
            string projectPath = BuildProjectReferencePath( fullPluginName );
            var projectRef = FindProjectReference( projectPath );
            if( projectRef != null )
            {
                wasProjectReference = true;
                projectRef.Remove();
            }
            var packageRef = FindPackageReference( fullPluginName );
            if( packageRef != null )
            {
                packageRef.Remove();
            }
            var packageVersion = FindPackageVersion( fullPluginName );
            if( packageVersion != null )
            {
                packageVersion.Remove();
            }
            RemoveRegisterCall( shortPluginName );
            return true;
        }

        public bool AddProjectReference( IActivityMonitor monitor, string shortPluginName, string fullPluginName )
        {
            if( FindPackageReference( fullPluginName ) != null )
            {
                monitor.Error( $"Plugin project '{fullPluginName}' is already referenced as a package in '{_machinery.CKliPluginsCSProj}'." );
                return false;
            }
            string projectPath = BuildProjectReferencePath( fullPluginName );
            if( FindProjectReference( projectPath ) != null )
            {
                monitor.Warn( $"Plugin project '{fullPluginName}' is already referenced in '{_machinery.CKliPluginsCSProj}'." );
            }
            else
            {
                _csFirstItemGroup.Add( new XElement( "ProjectReference",
                                        new XAttribute( "Include", projectPath ) ) );
                AddRegisterCall( shortPluginName );
            }
            return true;
        }

        public bool AddOrSetPackageReference( IActivityMonitor monitor, string shortPluginName, string fullPluginName, SVersion version )
        {
            string projectPath = BuildProjectReferencePath( fullPluginName );
            if( FindProjectReference( projectPath ) != null )
            {
                monitor.Error( $"Plugin project '{fullPluginName}' is already a project reference in '{_machinery.CKliPluginsCSProj}'." );
                return false;
            }
            XElement? existsRef = FindPackageReference( fullPluginName );
            if( existsRef == null )
            {
                _csFirstItemGroup.Add( new XElement( "PackageReference",
                                        new XAttribute( "Include", fullPluginName ) ) );
            }
            XElement? existsVer = FindPackageVersion( fullPluginName );
            if( existsVer == null )
            {
                existsVer = new XElement( "PackageVersion",
                                        new XAttribute( "Include", fullPluginName ), new XAttribute( "Version", version ) );
                _dpFirstItemGroup.Add( existsVer );
            }

            if( existsRef == null )
            {
                AddRegisterCall( shortPluginName );
            }
            return true;
        }

        static string BuildProjectReferencePath( string fullPluginName )
        {
            return $"..\\{fullPluginName}\\{fullPluginName}.csproj";
        }

        XElement? FindPackageReference( string fullPluginName )
        {
            return _csProj.Root!.Elements( "ItemGroup" )
                                         .Elements( "PackageReference" )
                                         .FirstOrDefault( e => e.Attribute( "Include" )?.Value == fullPluginName );
        }

        XElement? FindPackageVersion( string fullPluginName )
        {
            return _directoryPackages.Root!.Elements( "ItemGroup" )
                                                    .Elements( "PackageVersion" )
                                                    .FirstOrDefault( e => e.Attribute( "Include" )?.Value == fullPluginName );
        }

        XElement? FindProjectReference( string projectPath )
        {
            Throw.DebugAssert( projectPath.StartsWith( "..\\" ) && projectPath.EndsWith( ".csproj" ) );
            return _csProj.Root!.Elements( "ItemGroup" )
                                         .Elements( "PackageReference" )
                                         .FirstOrDefault( e => e.Attribute( "Include" )?.Value == projectPath );
        }

        internal bool Save( IActivityMonitor monitor ) 
        {
            try
            {
                File.WriteAllText( _machinery.CKliPluginsFile, _ckliPluginsFileText );
                _csProj.Save( _machinery.CKliPluginsCSProj );
                _directoryPackages.Save( _machinery.DirectoryPackageProps );
                return true;
            }
            catch( Exception ex )
            {
                monitor.Error( "While saving CKli.Plugins project.", ex );
                return false;
            }
        }

        void AddRegisterCall( string shortPluginName )
        {
            var register = $"{shortPluginName}.Register( collector );{Environment.NewLine}{new string( ' ', _linePrefixLength )}";
            _ckliPluginsFileText = _ckliPluginsFileText.Insert( _autoSectionEnd, register );
        }

        void RemoveRegisterCall( string shortPluginName )
        {
            Throw.DebugAssert( "No need to escape the shortPluginName (it is an identifier).",
                               PluginMachinery.IsValidShortPluginName( shortPluginName ) );
            _ckliPluginsFileText = Regex.Replace( _ckliPluginsFileText,
                                                  $"""{shortPluginName}\s*\.\s*Register\s*(\s*collector\s*)\s*;\s*""",
                                                  "",
                                                  RegexOptions.Singleline|RegexOptions.CultureInvariant ); 
        }
    }
}

