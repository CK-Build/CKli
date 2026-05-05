
using CK.Core;
using CKli.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CKli;

public static partial class CKliTestHelperExtensions
{
    /// <summary>
    /// Models a "Remotes/" folder that contains a stack repository and the repositories of the stack.
    /// </summary>
    public sealed partial class RemotesCollection
    {
        readonly string _fullName;
        readonly string[] _repositoryNames;
        readonly Uri _stackUri;
        readonly string _stackName;

        internal RemotesCollection( string fullName, string[] repositoryNames )
        {
            _fullName = fullName;
            _repositoryNames = repositoryNames;
            int idx = fullName.IndexOf( '(' );
            Throw.DebugAssert( idx != 0 );
            _stackName = idx < 0 ? fullName : fullName.Substring( 0, idx );
            _stackUri = GetUriFor( _stackName + "-Stack" );
        }

        /// <summary>
        /// Gets the full name (with the optional state name in parentheses).
        /// </summary>
        public string FullName => _fullName;

        /// <summary>
        /// Gets the "XXX-Stack" folder in the <see cref="FullName"/> folder.
        /// </summary>
        public NormalizedPath StackLocalFolderPath => _remotesPath.AppendPart( _fullName ).AppendPart( _stackName+"-Stack" );

        /// <summary>
        /// Gets the stack name. There must be a "StackName-Stack" repository folder that
        /// contains, at least the "StackName.xml" default world definition file in it.
        /// </summary>
        public string StackName => _stackName;

        /// <summary>
        /// Gets the Url of the remote Stack repository (in the "Remotes/bare/" folder)..
        /// </summary>
        public Uri StackUri => _stackUri;

        /// <summary>
        /// Gets all the repository names (including the "StackName-Stack").
        /// </summary>
        public IReadOnlyList<string> Repositories => _repositoryNames;

        /// <summary>
        /// Gets the Url for one of the <see cref="Repositories"/>.
        /// <para>
        /// When missing, a fake url "file:///Missing..." is returned that will trigger an error is used.
        /// </para>
        /// </summary>
        /// <param name="repositoryName">The repository name that should belong to the <see cref="Repositories"/>.</param>
        /// <returns>The url for the remote repository (in the "Remotes/bare/" folder).</returns>
        public Uri GetUriFor( string repositoryName )
        {
            if( _repositoryNames.Contains( repositoryName ) )
            {
                return new Uri( _barePath.AppendPart( _fullName ).AppendPart( repositoryName ) );
            }
            return new Uri( "file:///Missing '" + repositoryName + "' repository in '" + _fullName + "' remotes" );
        }

        /// <summary>
        /// <para>
        /// Returns a <see cref="CKliEnv"/> context for the default World of this RemotesCollection.
        /// </para>
        /// Clones these <see cref="Repositories"/> in the <paramref name="clonedFolder"/>'s <see cref="CKliEnv.CurrentDirectory"/>
        /// that must be inside the <see cref="CKliClonedPath"/> and:
        /// <list type="bullet">
        ///     <item>Copies the host's stack's default World <c>&lt;Plugins&gt;</c> configuration to the default World plugins.</item>
        ///     <item>Calls the <paramref name="pluginConfigurationEditor"/> if it is provided to alter the <c>&lt;Plugins&gt;</c> configuration.</item>
        ///     <item>Copies the "$Local" from the "Remotes/<see cref="FullName"/>/<see cref="StackName"/>-Stack/$Local".</item>
        /// </list>
        /// </summary>
        /// <param name="clonedFolder">The cloned folder of the unit test.</param>
        /// <param name="pluginConfigurationEditor">
        /// Optional plugin configuration editor, the path is the <paramref name="clonedFolder"/> and the XElement
        /// is the <c>&lt;Plugins&gt;</c> configuration.
        /// </param>
        /// <param name="privateStack">
        /// Whether to clone the stack as a private stack (in a ".PrivateStack" folder) or a public stack (in a ".PublicStack" folder).
        /// </param>
        /// <returns>The default world cloned context.</returns>
        public CKliEnv Clone( ClonedFolder clonedFolder, Action<IActivityMonitor, NormalizedPath, XElement>? pluginConfigurationEditor = null, bool privateStack = false )
        {
            Throw.DebugAssert( clonedFolder.Path.StartsWith( _clonedPath ) );

            var context = new CKliEnv( clonedFolder.Path, screen: new StringScreen(), findCurrentStackPath: false );
            CloneOrThrow( context, _stackUri, _stackName, privateStack );
            context = context.ChangeDirectory( _stackName );
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
                    pluginConfigurationEditor?.Invoke( monitor, clonedFolder.Path, plugins );
                } );
                world.DefinitionFile.SaveFile( TestHelper.Monitor );
                stack.Commit( TestHelper.Monitor, "Updated <Plugins> configuration." );
            }
            finally
            {
                stack.Dispose();
            }
            // Copy the $Local folder.
            var local = _remotesPath.AppendPart( _fullName )
                          .AppendPart( _stackName + "-Stack" )
                          .AppendPart( "$Local" );
            var source = new DirectoryInfo( local );
            if( source.Exists )
            {
                FileUtil.CopyDirectory( source, new DirectoryInfo( context.CurrentStackPath.AppendPart( "$Local" ) ) );
            }
            return context;

            static void CloneOrThrow( CKliEnv context, Uri stackUri, string stackName, bool privateStack )
            {
                using( var stack = StackRepository.Clone( TestHelper.Monitor,
                                                          context,
                                                          stackUri,
                                                          !privateStack,
                                                          allowDuplicateStack: false,
                                                          ignoreParentStack: true ) )
                {
                    if( stack == null )
                    {
                        Throw.CKException( $"Unable to open test stack '{stackName}' from '{stackUri}'." );
                    }
                }
            }
        }

        /// <summary>
        /// Overridden to return the full name and the number of repositories.
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => $"{_fullName} - {_repositoryNames.Length} repositories";
    }
}
