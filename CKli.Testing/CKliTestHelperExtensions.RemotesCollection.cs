
using CK.Core;
using CKli.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CKli;

public static partial class CKliTestHelperExtensions
{
    /// <summary>
    /// Models a "Remotes/" folder that contains a stack repository and the repositories of the stack:
    /// this is a local folder that ends with the <see cref="FullName"/> and contains bare repositories.
    /// </summary>
    public class RemotesFolder
    {
        readonly NormalizedPath _barePath;
        readonly Uri _stackUri;
        readonly string _stackName;

        /// <summary>
        /// Initializes a new folder for remotes.
        /// </summary>
        /// <param name="barePath">The path where bare Git repositories exists.</param>
        public RemotesFolder( NormalizedPath barePath )
        {
            _barePath = barePath;
            var fullName = barePath.LastPart;
            int idx = fullName.IndexOf( '(' );
            Throw.CheckArgument( "fullName must not start with a '('.", idx != 0 );
            _stackName = idx < 0 ? fullName : fullName.Substring( 0, idx );
            var stack = _stackName + "-Stack";
            _stackUri = GetUriFor( stack );
        }

        /// <summary>
        /// The root of the bare repositories. Ends with the <see cref="FullName"/>.
        /// </summary>
        public NormalizedPath BarePath => _barePath;

        /// <summary>
        /// Gets the full name (with the optional state name in parentheses).
        /// </summary>
        public string FullName => _barePath.LastPart;

        /// <summary>
        /// Gets the stack name. There must be a "StackName-Stack" repository folder that
        /// contains, at least the "StackName.xml" default world definition file in it.
        /// </summary>
        public string StackName => _stackName;

        /// <summary>
        /// Gets the Url of the remote Stack repository.
        /// </summary>
        public Uri StackUri => _stackUri;

        /// <summary>
        /// Gets all the repository names (including the "StackName-Stack").
        /// </summary>
        public IEnumerable<string> Repositories => Directory.EnumerateDirectories( _barePath ).Select( Path.GetFileName )!;

        /// <summary>
        /// Gets the Url for a repository.
        /// <para>
        /// When missing, a fake url "file:///Missing..." is returned that will trigger an error is used.
        /// </para>
        /// </summary>
        /// <param name="repositoryName">The repository name that should belong to the <see cref="Repositories"/>.</param>
        /// <returns>The url for the remote repository (in the "Remotes/bare/" folder).</returns>
        public Uri GetUriFor( string repositoryName )
        {
            var p = _barePath.AppendPart( repositoryName );
            return Directory.Exists( p )
                    ? new Uri( p )
                    : new Uri( "file:///Missing '" + repositoryName + "' repository in '" + FullName + "' remotes" );
        }

        /// <summary>
        /// Returns a <see cref="CKliEnv"/> context for the default World of this RemotesCollection.
        /// <para>
        /// Clones these <see cref="Repositories"/> in the <paramref name="clonedFolder"/>'s <see cref="ClonedFolder.Path"/> and:
        /// <list type="bullet">
        ///     <item>Copies the host's stack's default World <c>&lt;Plugins&gt;</c> configuration to the default World plugins.</item>
        ///     <item>Calls the <paramref name="pluginConfigurationEditor"/> if it is provided to alter the <c>&lt;Plugins&gt;</c> configuration.</item>
        ///     <item>Copies the "$Local" from the "Remotes/<see cref="FullName"/>/<see cref="StackName"/>-Stack/$Local".</item>
        /// </list>
        /// </para>
        /// </summary>
        /// <param name="clonedFolder">The cloned folder of the unit test.</param>
        /// <param name="pluginConfigurationEditor">
        /// Optional plugin configuration editor, the path is the <paramref name="clonedFolder"/>'s path and the XElement
        /// is the <c>&lt;Plugins&gt;</c> configuration.
        /// </param>
        /// <param name="privateStack">
        /// Stack access: clone the stack as a private stack (in a ".PrivateStack" folder) or a public stack (in a ".PublicStack" folder).
        /// </param>
        /// <returns>The default world cloned context.</returns>
        public Task<CKliEnv> CloneAsync( ClonedFolder clonedFolder, Action<IActivityMonitor, NormalizedPath, XElement>? pluginConfigurationEditor = null, bool privateStack = false )
                        => CloneAsync( clonedFolder.Path, false, pluginConfigurationEditor, privateStack );

        /// <summary>
        /// Implementation of <see cref="CloneAsync(ClonedFolder, Action{IActivityMonitor, NormalizedPath, XElement}?, bool)"/> that can be used in
        /// sub folders of the <see cref="ClonedFolder"/> to work with multiple clones of the same remote.
        /// </summary>
        /// <param name="folder">The cloned folder of the unit test or a subfolder of it.</param>
        /// <param name="allowDuplicateStack">
        /// First cloned repo can use false, but subsequent ones must specify true otherwise the duplicate is detected and an exception is thrown.
        /// </param>
        /// <param name="pluginConfigurationEditor">
        /// Optional plugin configuration editor, the path is the cloned <see cref="StackRepository.StackRoot"/> and the XElement
        /// is the <c>&lt;Plugins&gt;</c> configuration.
        /// <para>
        /// The last part of the StackRoot is the <see cref="StackRepository.DuplicatePrefix"/> with the stack name when the cloned stack
        /// is a duplicate.
        /// </para>
        /// </param>
        /// <param name="privateStack">
        /// Whether to clone the stack as a private stack (in a ".PrivateStack" folder) or a public stack (in a ".PublicStack" folder).
        /// </param>
        /// <returns>The default world cloned context.</returns>
        public async Task<CKliEnv> CloneAsync( NormalizedPath folder,
                                               bool allowDuplicateStack,
                                               Action<IActivityMonitor, NormalizedPath, XElement>? pluginConfigurationEditor = null,
                                               bool privateStack = false )
        {
            Throw.CheckArgument( folder.StartsWith( _clonedPath ) );

            var context = new CKliEnv( folder, screen: new StringScreen(), findCurrentStackPath: false );
            context = await CloneOrThrowAsync( context, _stackUri, _stackName, allowDuplicateStack, privateStack );
            if( !StackRepository.OpenWorldFromPath( TestHelper.Monitor,
                                                    context,
                                                    out var stack,
                                                    out var world,
                                                    skipPullStack: true,
                                                    withPlugins: false ) )
            {
                Throw.CKException( $"Unable to open default World of cloned test Stack from '{context.CurrentDirectory}'." );
            }
            try
            {
                // Injects Plugins configuration of the host world into the world definition of the world to test.
                world.DefinitionFile.RawEditPlugins( TestHelper.Monitor, ( monitor, plugins ) =>
                {
                    plugins.RemoveAll();
                    plugins.Add( _hostPluginsConfiguration.Attributes() );
                    plugins.Add( _hostPluginsConfiguration.Nodes() );
                    pluginConfigurationEditor?.Invoke( monitor, stack.StackRoot, plugins );
                } );
                world.DefinitionFile.SaveFile( TestHelper.Monitor );
                stack.Commit( TestHelper.Monitor, "Updated <Plugins> configuration." );
            }
            finally
            {
                stack.Dispose();
            }
            // Copy the $Local folder.
            var local = _remotesPath.AppendPart( FullName )
                          .AppendPart( _stackName + "-Stack" )
                          .AppendPart( "$Local" );
            var source = new DirectoryInfo( local );
            if( source.Exists )
            {
                FileUtil.CopyDirectory( source, new DirectoryInfo( context.CurrentStackPath.AppendPart( "$Local" ) ) );
            }
            return context;

            static async Task<CKliEnv> CloneOrThrowAsync( CKliEnv context, Uri stackUri, string stackName, bool allowDuplicateStack, bool privateStack )
            {
                using( var stack = await StackRepository.CloneAsync( TestHelper.Monitor,
                                                                     context,
                                                                     stackUri,
                                                                     !privateStack,
                                                                     allowDuplicateStack,
                                                                     ignoreParentStack: true ) )
                {
                    if( stack == null )
                    {
                        Throw.CKException( $"Unable to open test stack '{stackName}' from '{stackUri}'." );
                    }
                    context = context.ChangeDirectory( stack.StackRoot );
                }
                return context;
            }
        }

        /// <summary>
        /// Overridden to return the full name and the number of repositories.
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => $"{FullName} - {Repositories.Count()} repositories";
    }


}
