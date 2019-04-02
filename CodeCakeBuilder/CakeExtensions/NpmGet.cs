using System;
using System.Collections.Generic;
using Cake.Core;
using Cake.Core.Annotations;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using Cake.Core.Tooling;
using System.Linq;
namespace Cake.Npm
{
    public class NpmGetSettings : NpmSettings
    {
        public NpmGetSettings() : base( "get" )
        {
            RedirectStandardOutput = true;
        }
        public string Key { get; set; }

        protected override void EvaluateCore( ProcessArgumentBuilder args )
        {
            base.EvaluateCore( args );
            args.Append( Key );
        }
    }

    public class NpmGetTools : NpmTool<NpmSettings>
    {
        public NpmGetTools(
            IFileSystem fileSystem,
            ICakeEnvironment environment,
            IProcessRunner processRunner,
            IToolLocator tools,
            ICakeLog log )
            : base( fileSystem, environment, processRunner, tools, log )
        {
        }


        public string Get( NpmGetSettings settings )
        {
            if( string.IsNullOrWhiteSpace( settings.Key ) )
            {
                throw new ArgumentException();
            }
            IEnumerable<string> output = new List<string>();
            RunCore( settings, new ProcessSettings(), process =>
            {
                output = process.GetStandardOutput();
            } );
            return output.SingleOrDefault();
        }
    }
    [CakeAliasCategory( "Npm" )]
    [CakeNamespaceImport( "Cake.Npm" )]
    public static class NpmSetAliases
    {
        [CakeMethodAlias]
        [CakeAliasCategory( "Get" )]
        public static string NpmGet( this ICakeContext context, string key, string workingDirectory = null )
        {
            if( key == null )
            {
                throw new ArgumentNullException( "key can't be null" );
            }
            NpmGetSettings settings = new NpmGetSettings()
            {
                Key = key,
                WorkingDirectory = workingDirectory
            };
            return new NpmGetTools( context.FileSystem, context.Environment, context.ProcessRunner, context.Tools, context.Log ).Get( settings );
        }
    }
}
