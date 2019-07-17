using CK.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace CK.Env
{
    /// <summary>
    /// Registry of <see cref="IGitPlugin"/> and <see cref="IGitBranchPlugin"/> that apply
    /// to a <see cref="FolderPath"/>.
    /// The <see cref="GitPluginManager"/> of a <see cref="GitFolder"/> uses this registry to instanciate
    /// the plugins.
    /// </summary>
    public class GitPluginRegistry
    {
        readonly Dictionary<EntryKey, Descriptor> _descriptors;

        readonly struct EntryKey : IEquatable<EntryKey>
        {
            public readonly Type Type;
            public readonly string BranchName;

            public EntryKey( Type t, string b )
            {
                Type = t;
                BranchName = b;
            }

            public bool Equals( EntryKey other ) => Type == other.Type && BranchName == other.BranchName;

            public override bool Equals( object obj ) => obj is EntryKey k && Equals( k );

            public override int GetHashCode() => Type.GetHashCode() ^ (BranchName?.GetHashCode() ?? 0);

            public override string ToString() => $"'{BranchName}'/{Type.FullName}";
        }

        class Descriptor
        {
            public readonly EntryKey Key;
            public readonly ConstructorInfo Ctor;
            public readonly int BranchParameterIdx;
            public readonly ParameterInfo[] Parameters;
            public object Settings;

            public Descriptor( EntryKey k )
            {
                Key = k;
                Ctor = k.Type.GetConstructors().Single();
                BranchParameterIdx = -1;
                Parameters = Ctor.GetParameters();
                for( int i = 0; i < Parameters.Length; ++i )
                {
                    var p = Parameters[i];
                    if( IsGitFolderPlugin( p.ParameterType ) )
                    {
                        if( k.BranchName == null && IsGitFolderBranchPlugin( p.ParameterType ) )
                        {
                            throw new ArgumentException( $"Invalid plugin dependency: {k.Type.FullName} depends on {p.ParameterType.FullName} that is Branch dependent.", nameof( EntryKey ) );
                        }
                    }
                    else if( p.Name == "branchPath" && p.ParameterType == typeof( NormalizedPath ) )
                    {
                        if( k.BranchName == null )
                        {
                            throw new ArgumentException( $"Invalid plugin: {k.Type.FullName} is not a IGitBranchPlugin: it can not have a 'NormalizedPath branchPath' parameter.", nameof( EntryKey ) );
                        }
                        BranchParameterIdx = i;
                    }
                }
                if( BranchParameterIdx < 0 && k.BranchName != null )
                {
                    throw new ArgumentException( $"Constructor of {k.Type.FullName} must have a 'NormalizedPath branchPath' parameter.", nameof( EntryKey ) );
                }
            }

            public Descriptor( object instance, EntryKey k )
            {
                Debug.Assert( instance != null );
                Key = k;
                Settings = instance;
            }

            public override string ToString() => $"{Key} - {(Settings != null ? "Settings" : "Plugin")})";

        }

        /// <summary>
        /// Static helper that registers all the <see cref="IGitPlugin"/> concrete types
        /// available in the currently loaded assemblies.
        /// </summary>
        public static class GlobalRegister
        {
            static readonly HashSet<Assembly> _analyzed = new HashSet<Assembly>();
            static readonly List<Type> _plugins = new List<Type>();

            /// <summary>
            /// Gets the list of all the <see cref="IGitPlugin"/> concrete types
            /// available in the currently loaded assemblies.
            /// </summary>
            /// <returns>The Git plugin types.</returns>
            public static IReadOnlyList<Type> GetAllGitPlugins()
            {
                foreach( var a in AppDomain.CurrentDomain.GetAssemblies() )
                {
                    if( _analyzed.Add( a ) )
                    {
                        _plugins.AddRange( a.ExportedTypes.Where( t => !t.IsAbstract && typeof( IGitPlugin ).IsAssignableFrom( t ) ) );
                    }
                }
                return _plugins;
            }

        }


        /// <summary>
        /// Initializes a new <see cref="GitPluginRegistry"/>.
        /// </summary>
        /// <param name="folderPath">Path relative to <see cref="FileSystem.Root"/> and that contains the .git sub folder.</param>
        public GitPluginRegistry( in NormalizedPath folderPath )
        {
            if( folderPath.IsRooted ) throw new ArgumentException( "Must not be rooted.", nameof( folderPath ) );
            if( folderPath.LastPart == "branches" ) throw new ArgumentException( "Must not end with /branches.", nameof( folderPath ) );
            FolderPath = folderPath;
            BranchesPath = folderPath.AppendPart( "branches" );
            _descriptors = new Dictionary<EntryKey, Descriptor>();
        }

        /// <summary>
        /// Gets the path that is relative to <see cref="FileSystem.Root"/> and contains the .git sub folder.
        /// </summary>
        public NormalizedPath FolderPath { get; }

        /// <summary>
        /// Get the path to which the registered plugins apply.
        /// It is <see cref="FolderPath"/> with a trailing "/branches" part.
        /// </summary>
        public NormalizedPath BranchesPath { get; }

        /// <summary>
        /// Registers a settings object.
        /// The instance will be available in all branches if <paramref name="branchName"/> is null.
        /// Note that if an instance has already been registered, it is replaced.
        /// </summary>
        /// <param name="type">The type to register. Must not be null.</param>
        /// <param name="instance">The instance. Must not be null.</param>
        /// <param name="branchName">The branch name or null for a root setting.</param>
        public void RegisterSettings( Type type, object instance, string branchName = null )
        {
            if( type == null ) throw new ArgumentNullException( nameof( type ) );
            if( instance == null ) throw new ArgumentNullException( nameof( instance ) );
            if( IsGitFolderPlugin( type ) )
            {
                throw new Exception( $"A plugin cannot be registered as a setting: {type.FullName}." );
            }
            var key = new EntryKey( type, branchName );
            if( !_descriptors.TryGetValue( key, out var desc ) )
            {
                desc = new Descriptor(instance, key);
                _descriptors.Add( key, desc );
            }
            else if( desc.Settings != null )
            {
                desc.Settings = instance;
            }
        }

        /// <summary>
        /// Registers a settings object.
        /// The instance will be available in all branches if <paramref name="branchName"/> is null.
        /// Note that if an instance has already been registered, it is replaced.
        /// </summary>
        /// <typeparam name="T">Type of the settings.</typeparam>
        /// <param name="instance">The instance. Must not be null.</param>
        /// <param name="branchName">The branch name or null for a root setting.</param>
        public void RegisterSettings<T>( T instance, string branchName = null ) => RegisterSettings( typeof( T ), instance, branchName );

        /// <summary>
        /// Registers a type that must be <see cref="IGitPlugin"/>.
        /// The plugin will be available in all branches.
        /// </summary>
        /// <param name="pluginType">The type of the plugin. Must be a non null <see cref="IGitPlugin"/>.</param>
        public void Register( Type pluginType )
        {
            CheckPluginType( pluginType );
            DoRegister( pluginType, null );
        }

        /// <summary>
        /// Registers a type that must be <see cref="IGitBranchPlugin"/> (or a <see cref="IGitPlugin"/> if allowed).
        /// A branch plugin will be available only in the specific branch.
        /// </summary>
        /// <param name="pluginType">
        /// The type of the plugin. Must be a non null <see cref="IGitBranchPlugin"/> or <see cref="IGitPlugin"/>
        /// (if <paramref name="allowGitPlugin"/> is true).
        /// </param>
        /// <param name="branchName">Branch name. Must not be null.</param>
        /// <param name="allowGitPlugin">True to allow <see cref="IGitPlugin"/> to be registered.</param>
        public void Register( Type pluginType, string branchName, bool allowGitPlugin )
        {
            if( CheckBranchPluginType( pluginType, branchName, allowGitPlugin ) )
            {
                DoRegister( pluginType, branchName );
            }
            else
            {
                DoRegister( pluginType, null );
            }
        }

        Descriptor DoRegister( Type pluginType, string branchName )
        {
            var key = new EntryKey( pluginType, branchName );
            if( !_descriptors.TryGetValue( key, out var desc ) )
            {
                desc = new Descriptor( key );
                _descriptors.Add( key, desc );
            }
            return desc;
        }

        internal int FillMappings(
            Dictionary<Type, object> mappings,
            IServiceProvider baseProvider,
            CommandRegister commandRegister,
            string branchName,
            string defaultBranchName )
        {
            Debug.Assert( (branchName != null) == (defaultBranchName != null) );
            Debug.Assert( !mappings.Keys.Any( k => IsGitFolderPlugin( k ) ), "There must not be any plugin. Only settings." );

            int pluginCount = 0;
            IEnumerable<Descriptor> forBranch = _descriptors.Values.Where( d => d.Key.BranchName == branchName
                                                       || (defaultBranchName != null
                                                           && d.Key.BranchName == defaultBranchName
                                                           && !_descriptors.ContainsKey( new EntryKey( d.Key.Type, branchName ) )) );
            foreach( var desc in forBranch )
            {
                if( !mappings.TryGetValue( desc.Key.Type, out var obj ) )
                {
                    obj = desc.Settings
                            ?? CreateInstance( baseProvider, desc, branchName, defaultBranchName, mappings, commandRegister, ref pluginCount );
                    mappings.Add( desc.Key.Type, obj );
                }
            }
            return pluginCount;
        }

        object CreateInstance(
            IServiceProvider provider,
            Descriptor desc,
            string branchName,
            string defaultBranchName,
            Dictionary<Type, object> mappings,
            CommandRegister commandRegister,
            ref int pluginCount )
        {
            Debug.Assert( (branchName != null) == (defaultBranchName != null) );
            Debug.Assert( !mappings.ContainsKey( desc.Key.Type ) );
            Debug.Assert( desc.Settings == null );
            ++pluginCount;
            var parameters = new object[desc.Parameters.Length];
            for( int i = 0; i < parameters.Length; ++i )
            {
                if( i == desc.BranchParameterIdx )
                {
                    parameters[i] = BranchesPath.Combine( branchName );
                }
                else
                {
                    var pType = desc.Parameters[i].ParameterType;
                    if( mappings.TryGetValue( pType, out var already ) )
                    {
                        parameters[i] = already;
                    }
                    else
                    {
                        Descriptor pDesc = FindBestDescriptor( pType, branchName, defaultBranchName );
                        if( pDesc != null )
                        {
                            object obj = pDesc.Settings
                                            ?? CreateInstance( provider, pDesc, branchName, defaultBranchName, mappings, commandRegister, ref pluginCount );
                            mappings.Add( pDesc.Key.Type, obj );
                            parameters[i] = obj;
                        }
                        else
                        {
                            parameters[i] = provider.GetService( pType );
                            if( parameters[i] == null ) throw new Exception( $"Unable to resolve '{pType}' for {desc.Key.Type.FullName} plugin constructor." );
                        }
                    }
                }
            }
            object o = Activator.CreateInstance( desc.Key.Type, parameters );
            if( o is ICommandMethodsProvider c )
            {
                commandRegister.Register( c );
            }
            return o;
        }

        Descriptor FindBestDescriptor( Type type, string branchName, string defaultBranchName )
        {
            Debug.Assert( type != null );
            Debug.Assert( (branchName != null) == (defaultBranchName != null) );

            EntryKey key = new EntryKey( type, branchName );
            if( !_descriptors.TryGetValue( key, out var desc ) )
            {
                if( branchName != null )
                {
                    key = new EntryKey( type, defaultBranchName );
                    _descriptors.TryGetValue( key, out desc );
                }
            }
            return desc;
        }

        internal static void CheckPluginType( Type pluginType )
        {
            if( pluginType == null ) throw new ArgumentNullException( nameof( pluginType ) );
            if( !IsGitFolderPlugin( pluginType ) ) throw new ArgumentException( $"Must be a IGitPlugin: {pluginType.FullName}", nameof( pluginType ) );
            if( IsGitFolderBranchPlugin( pluginType ) ) throw new ArgumentException( $"Must not be a IGitBranchPlugin: {pluginType.FullName}", nameof( pluginType ) );
        }

        internal static bool CheckBranchPluginType( Type pluginType, string branchName, bool allowGitPlugin )
        {
            if( pluginType == null ) throw new ArgumentNullException( nameof( pluginType ) );
            if( String.IsNullOrWhiteSpace( branchName ) ) throw new ArgumentNullException( nameof( branchName ) );
            if( !IsGitFolderBranchPlugin( pluginType ) )
            {
                if( !allowGitPlugin ) throw new ArgumentException( $"Must be a IGitBranchPlugin: {pluginType.FullName}.", nameof( pluginType ) );
                CheckPluginType( pluginType );
                return false;
            }
            return true;
        }

        internal static bool IsGitFolderPlugin( Type t ) => typeof( IGitPlugin ).IsAssignableFrom( t );

        internal static bool IsGitFolderBranchPlugin( Type t ) => typeof( IGitBranchPlugin ).IsAssignableFrom( t );
    }
}
