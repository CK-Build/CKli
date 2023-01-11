using CK.Core;
using CSemVer;
using Newtonsoft.Json.Linq;
using System.Linq;

namespace CK.Env.NodeSln
{
    public partial class PackageJsonFile
    {

        internal static PackageJsonFile? Read( IActivityMonitor monitor, NodeProjectBase project )
        {
            var filePath = project.Path.AppendPart( "package.json" );
            var file = project.Solution.FileSystem.GetFileInfo( filePath );
            if( !file.Exists || file.IsDirectory )
            {
                monitor.Warn( $"File '{filePath}' not found. Unable to read the Package.json." );
                return null;
            }
            var o = file.ReadAsJObject();

            if( !TryReadString( monitor, filePath, o, "name", out var name ) ) return null;

            bool isPrivate = false;
            if( !TryReadBoolean( monitor, filePath, o, "isPrivate", ref isPrivate ) ) return null;
            if( !isPrivate && name == null )
            {
                monitor.Error( $"File '{filePath}': property \"name\" must be specified since there is no \"private\": true." );
                return null;
            }

            bool isCKArtifact = false;
            if( !TryReadBoolean( monitor, filePath, o, "isCKArtifact", ref isCKArtifact ) ) return null;
            if( isCKArtifact && name == null )
            {
                monitor.Error( $"File '{filePath}': property \"name\" must be specified since \"isCKArtifact\": true." );
                return null;
            }

            if( !TryReadString( monitor, filePath, o, "version", out var sVersion ) ) return null;
            SVersion version = SVersion.ZeroVersion;
            if( sVersion != null && !SVersion.TryParse( sVersion, out version ) )
            {
                monitor.Error( $"File '{filePath}': property \"version\" is invalid: {version.ErrorMessage}." );
                return null;
            };

            NormalizedPath[]? workspaces = null;
            var pWorkspaces = o.Property( "workspaces" );
            if( pWorkspaces != null )
            {
                if( pWorkspaces.Value is JArray a && a.Count > 0 )
                {
                    if( !a.All( t => t.Type == JTokenType.String )
                        || a.Any( t => t.Value<string>()!.Contains( '*' )
                                       || t.Value<string>()!.Contains( '?' )
                                       || string.IsNullOrWhiteSpace( t.Value<string>() ) ) )
                    {
                        monitor.Error( $"File '{filePath}': property \"workspaces\" must contain non empty paths to sub folders. No glob pattern is supported." );
                        return null;
                    }
                    workspaces = a.Select( t => t.Value<string>()! ).Distinct().Select( s => new NormalizedPath( s ) ).ToArray();
                }
                else
                {
                    monitor.Warn( $"File '{filePath}': property \"workspaces\" is not an array or is an empty array. It is ignored." );
                }
            }

            var p = new PackageJsonFile( filePath, o, project, isPrivate, name, version, isCKArtifact, workspaces );
            return p.Initialize( monitor ) ? p : null;

            static bool TryReadString( IActivityMonitor monitor, NormalizedPath filePath, JObject o, string name, out string? value )
            {
                value = null;
                var pName = o.Property( name );
                if( pName != null )
                {
                    if( pName.Value.Type != JTokenType.String && pName.Value.Type != JTokenType.Null )
                    {
                        monitor.Error( $"File '{filePath}': property \"{name}\" must be a string." );
                        return false;
                    }
                    value = (string?)pName.Value;
                    if( value != null )
                    {
                        value = value.Trim();
                        if( value.Length == 0 ) value = null;
                    }
                }
                return true;
            }

            static bool TryReadBoolean( IActivityMonitor monitor, NormalizedPath filePath, JObject o, string name, ref bool value )
            {
                var pName = o.Property( name );
                if( pName != null )
                {
                    if( pName.Value.Type != JTokenType.Boolean )
                    {
                        monitor.Error( $"File '{filePath}': property \"{name}\" must be a true or false." );
                        return false;
                    }
                    value = (bool)pName.Value;
                }
                return true;
            }
        }
    }
}


