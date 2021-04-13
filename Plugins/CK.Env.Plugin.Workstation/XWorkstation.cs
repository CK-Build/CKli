using CK.Core;
using CK.Env;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CKli
{
    public class XWorkstation : XTypedObject, ICommandMethodsProvider
    {
        static readonly NormalizedPath _commandName = "Workstation";
        readonly FileSystem _fileSystem;

        enum ScriptType : byte
        {
            PS1
        }

        class ScriptLine
        {
            public readonly string Script;
            public readonly ScriptType Type;
            public readonly PlatformID Platform;
            public readonly NormalizedPath WorkingDir;
            public readonly string Arguments;
            public readonly bool ContinueOnNonZeroExitCode;

            public ScriptLine( string s, ScriptType t, PlatformID p, NormalizedPath w, string a, bool continueOnNonZeroExitCode )
            {
                Script = s;
                Type = t;
                Platform = p;
                WorkingDir = w;
                Arguments = a;
                ContinueOnNonZeroExitCode = continueOnNonZeroExitCode;
            }
        }

        readonly struct EnvVar : IEquatable<EnvVar>
        {
            public readonly string Name;
            public readonly string Value;

            public EnvVar( XElementReader r, bool valueRequired )
            {
                Name = r.HandleRequiredAttribute<string>( "Name" );
                Value = valueRequired
                            ? r.HandleRequiredAttribute<string>( "Value" )
                            : r.HandleOptionalAttribute<string>( "Value", null );
            }

            public bool Equals( EnvVar other ) => Name.Equals( other.Name, StringComparison.OrdinalIgnoreCase );

            public override bool Equals( object obj ) => obj is EnvVar e ? Equals( e ) : false;

            public override int GetHashCode() => Name.GetHashCode();

            public override string ToString() => $"{Name} = {Value}";
        }

        readonly List<object> _scripts;

        public XWorkstation( Initializer initializer,
            CommandRegister commands,
            FileSystem fileSystem )
            : base( initializer )
        {
            _scripts = new List<object>();
            _fileSystem = fileSystem;
            var envVars = new HashSet<EnvVar>();
            if( initializer.Reader.WithOptionalChild( "Setup", out XElementReader rSettings ) )
            {
                foreach( var r in rSettings.WithChildren() )
                {
                    if( r.Element.Name == "Script" )
                    {
                        r.Handle( r.Element );
                        HandleScriptElement( r );
                    }
                    else if( r.Element.Name == "EnvironmentVariables" )
                    {
                        r.Handle( r.Element );
                        envVars = r.HandleAddRemoveClearChildren( new HashSet<EnvVar>( envVars ), rE => new EnvVar( rE, valueRequired: rE.Element.Name == "add" ) );
                        _scripts.Add( envVars );
                    }
                }
            }
            commands.Register( this );
        }

        void HandleScriptElement( XElementReader r )
        {
            var p = r.HandleOptionalAttribute( "Platform", (PlatformID)0 );
            if( p != (PlatformID)0 && p != Environment.OSVersion.Platform )
            {
                r.Monitor.Info( $"Skipping one script since Platform is '{p}' (current is '{Environment.OSVersion.Platform}')." );
            }
            else
            {
                var w = new NormalizedPath( r.HandleOptionalAttribute( "WorkingDir", String.Empty ) );
                if( w.IsRooted ) r.ThrowXmlException( $"WorkingDir attribute must be a relative path: '{w}' is rooted." );
                w = _fileSystem.Root.Combine( w ).ResolveDots( _fileSystem.Root.Parts.Count );
                var t = r.HandleOptionalAttribute( "Type", ScriptType.PS1 );
                var a = r.HandleOptionalAttribute( "Arguments", String.Empty );
                var c = r.HandleOptionalAttribute( "ContinueOnNonZeroExitCode", false );
                string script;
                var url = r.HandleOptionalAttribute<string>( "Url", null );
                if( url != null )
                {
                    script = $@"
$Script = Invoke-WebRequest '{url}'
$ScriptBlock = [Scriptblock]::Create($Script.Content)
Invoke-Command -ScriptBlock $ScriptBlock";
                    if( a.Length > 0 )
                    {
                        script += $" -ArgumentList ($args + @('{a}'))";
                    }
                    a = String.Empty;
                    if( !String.IsNullOrEmpty( r.Element.Value ) ) r.ThrowXmlException( "Script element must be empty when Url attribute is used." );
                }
                else script = r.Element.Value;
                _scripts.Add( new ScriptLine( script, t, p, w, a, c ) );
            }
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => _commandName;

        public bool CanSetup => _scripts.Count > 0;

        [CommandMethod]
        public void Setup( IActivityMonitor monitor )
        {
            using( monitor.OpenInfo( $"Executing {_scripts.Count} script(s) on this Workstation." ) )
            using( var tempPS1 = new TemporaryFile( "ps1" ) )
            {
                bool hasError = false;
                HashSet<EnvVar> currentVariables = null;
                foreach( var o in _scripts )
                {
                    if( hasError ) break;
                    switch( o )
                    {
                        case HashSet<EnvVar> v: currentVariables = v; break;
                        case ScriptLine script:
                            {
                                using( monitor.OpenTrace( $"Executing script Type='{script.Type}', WorkingDir='{script.WorkingDir}', Arguments='{script.Arguments}', ContinueOnNonZeroExitCode='{script.ContinueOnNonZeroExitCode}'." ) )
                                {
                                    monitor.Debug( $"With EnvironmentVariables: {currentVariables?.Select( v => v.ToString() ).Concatenate()}." );
                                    monitor.Debug( script.Script );
                                    var variables = currentVariables?.Select( v => (v.Name, Environment.ExpandEnvironmentVariables( v.Value )) ).ToList()
                                        ?? new List<(string Name, string)>();
                                    variables.Add( ("CKLI_WORLD_MAPPING", _fileSystem.Root) );

                                    System.IO.File.WriteAllText( tempPS1.Path, script.Script );
                                    if( !ProcessRunner.RunPowerShell(
                                                monitor,
                                                script.WorkingDir,
                                                tempPS1.Path,
                                                new[] { script.Arguments },
                                                5 * 60 * 1000,
                                                stdErrorLevel: LogLevel.Warn,
                                                variables ) )
                                    {
                                        hasError |= !script.ContinueOnNonZeroExitCode;
                                        if( !hasError ) monitor.Warn( "ContinueOnNonZeroExitCode is true: error is ignored." );
                                    }
                                }
                                break;
                            }
                    }
                }
            }
        }
    }
}
