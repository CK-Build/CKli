using System.Collections.Generic;
using Cake.Common.IO;
using Cake.Core.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using System.IO;
using Cake.Core;
using Cake.Core.Annotations;

namespace Cake.Npm
{
    static class NpmHelper
    {
        [CakeMethodAlias]
        [CakeAliasCategory( "GetProjectsToPublish" )]
        public static IEnumerable<string> NpmGetProjectsToPublish( this ICakeContext cake )
        {
            var files = cake.GetFiles(//TODO Cake extension
                "**/package.json", //get all packages.json
                new GlobberSettings()
                {
                    Predicate = ( fsInfo ) => !fsInfo.Path.FullPath.Contains( "node_modules" )
                } );
            return files.Select( s => s.FullPath )//exclude the ones inside node_modules
                .Where( ( path ) => {
                    JObject a = JObject.Parse( File.ReadAllText( path ) ); //load the json 
                    bool value = a.Value<bool?>( "private" ) ?? false;
                    return !value;
                } )
                .Select( path => Directory.GetParent( path ).FullName );
        }
    }
}
