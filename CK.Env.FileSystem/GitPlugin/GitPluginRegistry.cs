using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    /// <summary>
    /// Internal, hidden behind <see cref="GitPluginManager"/>.
    /// </summary>
    class GitPluginRegistry
    {
        readonly Dictionary<PluginKey, Descriptor> _descriptors;

        readonly struct PluginKey : IEquatable<PluginKey>
        {
            public readonly Type Type;
            public readonly string BranchName;

            public PluginKey( Type t, string b )
            {
                Debug.Assert( t != null
                              && IsGitFolderPlugin( t )
                              && (IsGitFolderBranchPlugin( t ) == (b != null)) );    
                Type = t;
                BranchName = b;
            }

            public bool Equals( PluginKey other ) => Type == other.Type && BranchName == other.BranchName;

            public override bool Equals( object obj ) => obj is PluginKey k && Equals( k );

            public override int GetHashCode() => Type.GetHashCode() ^ (BranchName?.GetHashCode() ?? 0);
        }

        class Descriptor
        {
            public readonly PluginKey Key;
            public readonly ConstructorInfo Ctor;
            public readonly int BranchParameterIdx;
            readonly ParameterInfo[] _parameters;
            public object Settings;

            public Descriptor( PluginKey k, Type origin, object instance )
            {
                Key = k;
                Ctor = k.Type.GetConstructors().Single();
                _parameters = Ctor.GetParameters();
                if( typeof( IGitBranchPlugin ).IsAssignableFrom( k.Type ) )
                {
                    BranchParameterIdx = _parameters.IndexOf( p => p.Name == "branchName" && p.ParameterType == typeof( string ) );
                    if( BranchParameterIdx < 0 )
                    {
                        throw new ArgumentException( $"Constructor of {k.Type.FullName} must have a string branchName parameter.", nameof( PluginKey ) );
                    }
                }
                BranchParameterIdx = -1;
                for( int i = 0; i < _parameters.Length; ++i )
                {
                    var p = _parameters[i];
                    if( IsGitFolderPlugin( p.ParameterType ) )
                    {
                        if( p.ParameterType == origin ) throw new ArgumentException( $"Invalid plugin graph: cycle between {k.Type.FullName} and {origin.FullName}.", nameof( PluginKey ) );
                        bool isParamOnBranch = IsGitFolderBranchPlugin( p.ParameterType );
                        if( isParamOnBranch && k.BranchName == null )
                        {
                            throw new ArgumentException( $"Invalid plugin dependency: {k.Type.FullName} depends on {p.ParameterType.FullName} that is Branch dependent.", nameof( PluginKey ) );
                        }
                    }
                    else if( p.Name == "branchName" && p.ParameterType == typeof( string ) )
                    {
                        if( k.BranchName == null )
                        {
                            throw new ArgumentException( $"Invalid plugin: {k.Type.FullName} is not a IGitFolderBranchPlugin: it can not have a string branchName parameter.", nameof( PluginKey ) );
                        }
                        BranchParameterIdx = i;
                    }
                }
                if( BranchParameterIdx < 0 && k.BranchName != null )
                {
                    throw new ArgumentException( $"Constructor of {k.Type.FullName} must have a string branchName parameter.", nameof( PluginKey ) );
                }
            }

            public Descriptor( PluginKey k, object instance )
            {
                Debug.Assert( instance != null );
                Key = k;
                Settings = instance;
            }

            public IReadOnlyList<ParameterInfo> Parameters => _parameters;
        }

        public GitPluginRegistry()
        {
            _descriptors = new Dictionary<PluginKey, Descriptor>();
        }

        public void RegisterSettings( Type type, object instance, string branchName = null )
        {
            if( type == null ) throw new ArgumentNullException( nameof( type ) );
            if( instance == null ) throw new ArgumentNullException( nameof( instance ) );
            if( IsGitFolderPlugin( type ) )
            {
                throw new Exception( $"A plugin cannot be registered as an instance: {type.FullName}." );
            }
            var key = new PluginKey( type, branchName );
            if( !_descriptors.TryGetValue( key, out var desc ) )
            {
                desc = new Descriptor( key, instance );
                _descriptors.Add( key, desc );
            }
            else if( desc.Settings != null )
            {
                desc.Settings = instance;
            }
        }

        public void RegisterSettings( object instance, string branchName = null ) => RegisterSettings( instance?.GetType(), branchName );

        public void Register( Type pluginType )
        {
            CheckPluginType( pluginType );
            Register( pluginType, null, pluginType );
        }

        public void Register( Type pluginType, string branchName )
        {
            CheckBranchPluginType( pluginType, branchName );
            Register( pluginType, branchName, pluginType );
        }

        Descriptor Register( Type pluginType, string branchName, Type origin )
        {
            var key = new PluginKey( pluginType, branchName );
            if( !_descriptors.TryGetValue( key, out var desc ) )
            {
                desc = new Descriptor( key, origin );
                _descriptors.Add( key, desc );
            }
            return desc;
        }

        internal int FillMappings( Dictionary<Type, object> mappings, IServiceProvider baseProvider, string branchName, string defaultBranchName )
        {
            Debug.Assert( (branchName != null) == (defaultBranchName != null) );
            Debug.Assert( !mappings.Keys.Any( k => IsGitFolderPlugin( k ) ), "There must not be any plugin. Only settings." );

            int pluginCount = 0;
            IEnumerable<Descriptor> forBranch = _descriptors.Values.Where( d => d.Key.BranchName == branchName
                                                       || (defaultBranchName != null
                                                           && d.Key.BranchName == defaultBranchName
                                                           && !_descriptors.ContainsKey( new PluginKey( d.Key.Type, branchName ) )) );
            foreach( var desc in forBranch )
            {
                if( !mappings.TryGetValue( desc.Key.Type, out var obj ) )
                {
                    obj = desc.Settings
                            ?? CreateInstance( baseProvider, desc, branchName, defaultBranchName, mappings, ref pluginCount );
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
            Dictionary<Type, object> container,
            ref int pluginCount )
        {
            Debug.Assert( (branchName != null) == (defaultBranchName != null) );
            Debug.Assert( !container.ContainsKey( desc.Key.Type ) );
            Debug.Assert( desc.Settings == null );
            ++pluginCount;
            var parameters = new object[desc.Parameters.Count];
            for( int i = 0; i < parameters.Length; ++i )
            {
                if( i == desc.BranchParameterIdx ) parameters[i] = branchName;
                else
                {
                    var pType = desc.Parameters[i].ParameterType;
                    if( container.TryGetValue( pType, out var already ) )
                    {
                        parameters[i] = already;
                    }
                    else
                    {
                        Descriptor pDesc = FindBestDescriptor( pType, branchName, defaultBranchName );
                        if( pDesc != null )
                        {
                            object obj = pDesc.Settings
                                         ?? CreateInstance( provider, pDesc, branchName, defaultBranchName, container, ref pluginCount );
                            container.Add( pDesc.Key.Type, obj );
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
            return Activator.CreateInstance( desc.Key.Type, parameters );
        }

        Descriptor FindBestDescriptor( Type type, string branchName, string defaultBranchName )
        {
            Debug.Assert( type != null );
            Debug.Assert( (branchName != null) == (defaultBranchName != null) );

            PluginKey key = new PluginKey( type, branchName );
            if( !_descriptors.TryGetValue( key, out var desc ) )
            {
                if( branchName != null )
                {
                    key = new PluginKey( type, defaultBranchName );
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

        internal static void CheckBranchPluginType( Type pluginType, string branchName )
        {
            if( pluginType == null ) throw new ArgumentNullException( nameof( pluginType ) );
            if( String.IsNullOrWhiteSpace( branchName ) ) throw new ArgumentNullException( nameof( branchName ) );
            if( !IsGitFolderBranchPlugin( pluginType ) ) throw new ArgumentException( $"Must be a IGitBranchPlugin: {pluginType.FullName}.", nameof( pluginType ) );
        }

        internal static bool IsGitFolderPlugin( Type t ) => typeof( IGitPlugin ).IsAssignableFrom( t );

        internal static bool IsGitFolderBranchPlugin( Type t ) => typeof( IGitBranchPlugin ).IsAssignableFrom( t );
    }
}
