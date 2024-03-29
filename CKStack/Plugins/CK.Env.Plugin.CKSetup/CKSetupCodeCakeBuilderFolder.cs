using CK.Core;
using CK.Env.CKSetup;
using CK.Env.DependencyModel;

using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CK.Env.Plugin
{
    public class CKSetupCodeCakeBuilderFolder : PluginFolderBase, IDisposable
    {
        readonly SolutionDriver _driver;
        readonly SolutionSpec _solutionSpec;
        readonly CodeCakeBuilderKeyVaultFile _keyVaultFile;

        public CKSetupCodeCakeBuilderFolder( GitRepository f,
                                             SolutionDriver driver,
                                             CodeCakeBuilderKeyVaultFile keyVaultFile,
                                             SolutionSpec solutionSpec,
                                             NormalizedPath branchPath )
            : base( f, branchPath, "CodeCakeBuilder", "CKSetup/Res" )
        {
            _driver = driver;
            _solutionSpec = solutionSpec;
            _keyVaultFile = keyVaultFile;
            _driver.OnSolutionConfiguration += OnSolutionConfiguration;
            _keyVaultFile.Updating += KeyVaultFileUpdating;
        }

        void KeyVaultFileUpdating( object? sender, CodeCakeBuilderKeyVaultUpdatingArgs e )
        {
            CKSetupStore? store = e.SolutionSpec.UseCKSetup
                                    ? e.Solution.ArtifactTargets.OfType<CKSetupStore>().SingleOrDefault()
                                    : null;
            if( store != null )
            {
                if( e.Secrets.TryGetValue( store.SecretKeyName, out var apiKey ) )
                {
                    // The actual key contains both the url and the secret.
                    e.Secrets["CKSETUP_CAKE_TARGET_STORE_APIKEY_AND_URL"] = apiKey + '|' + store.Url;
                    e.Secrets.Remove( store.SecretKeyName );
                }
                else e.Monitor.Warn( $"Missing '{store.SecretKeyName}' secret. Unable to configure 'CKSETUP_CAKE_TARGET_STORE_APIKEY_AND_URL' in 'CodeCakeBuilderKeyVault.txt' file." );
            }
        }

        void OnSolutionConfiguration( object? sender, SolutionConfigurationEventArgs e )
        {
            bool targetCKSetup = e.Solution.GeneratedArtifacts.Any( a => a.Artifact.Type == CKSetupClient.CKSetupType );
            var stores = e.Solution.ArtifactTargets.OfType<CKSetupStore>().ToList();
            if( !targetCKSetup && stores.Any() )
            {
                e.Monitor.Info( $"Removing {stores.Select( s => s.UniqueRepositoryName ).Concatenate()} since no CKSetup artifacts are produced." );
                foreach( var t in stores ) e.Solution.RemoveArtifactTarget( t );
            }
        }

        /// <summary>
        /// Gets the name of this command: it is "<see cref="FolderPath"/>(CKSetup)".
        /// </summary>
        /// <returns>The command name.</returns>
        protected override NormalizedPath GetCommandProviderName() => FolderPath.AppendPart( "(CKSetup)" );


        protected override void DoApplySettings( IActivityMonitor m )
        {
            var s = _driver.GetSolution( m, allowInvalidSolution: true );
            if( s == null ) return;

            bool produceCKSetupComponents = s.GeneratedArtifacts.Any( g => g.Artifact.Type == CKSetupClient.CKSetupType );
            if( produceCKSetupComponents == true )
            {
                m.Info( "Adding Build.StandardPushCKSetupComponents.cs since CKSetup components are produced." );
                SetTextResource( m, "Build.StandardPushCKSetupComponents.cs", text => AdaptStandardStandardPushCKSetupComponents( m, s, text ) );
            }
            else
            {
                DeleteFileOrFolder( m, "Build.StandardPushCKSetupComponents.cs" );
            }
        }

        string AdaptStandardStandardPushCKSetupComponents( IActivityMonitor monitor, ISolution solution, string text )
        {
            Match m = Regex.Match( text, @"return new CKSetupComponent\[\]{.*?};", RegexOptions.Singleline | RegexOptions.CultureInvariant );
            if( !m.Success )
            {
                throw new Exception( "Expected pattern return new CKSetupComponent[]{...} in Build.StandardPushCKSetupComponents.cs." );
            }
            var comps = solution.GeneratedArtifacts.Where( g => g.Artifact.Type.Name == "CKSetup" );
            Debug.Assert( comps.Any() );
            StringBuilder b = new StringBuilder();
            b.AppendLine( "return new CKSetupComponent[]{" );
            bool atLeastOne = false;
            foreach( var c in comps )
            {
                b.AppendLine( atLeastOne ? "," : "" );
                atLeastOne = true;
                b.Append( "new CKSetupComponent( \"" )
                        .Append( c.Project.SolutionRelativeFolderPath )
                        .Append( "\", \"" )
                        .Append( c.Artifact.Name.Split( '/' )[1] )
                        .Append( "\" )" );
            }
            b.AppendLine().Append( "};" );
            text = text.Replace( m.Value, b.ToString() );
            return text;

        }

        void IDisposable.Dispose()
        {
            _driver.OnSolutionConfiguration -= OnSolutionConfiguration;
            _keyVaultFile.Updating -= KeyVaultFileUpdating;
        }
    }
}
