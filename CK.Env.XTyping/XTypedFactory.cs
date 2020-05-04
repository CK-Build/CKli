using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using System.Xml.XPath;

namespace CK.Env
{
    public class XTypedFactory
    {
        readonly XTypedFactory? _base;
        readonly Dictionary<XName, Type> _typeRegister;

        public XTypedFactory( XTypedFactory? baseFactory = null )
        {
            _base = baseFactory;
            _typeRegister = new Dictionary<XName, Type>();
        }

        public bool RegisterName( IActivityMonitor monitor, XName n, Type t, bool throwOnConflict = true )
        {
            if( n == null ) throw new ArgumentNullException( nameof( n ) );
            if( t == null ) throw new ArgumentNullException( nameof( t ) );
            return DoRegister( monitor, n, t, throwOnConflict );
        }

        /// <summary>
        /// Registers a set of <see cref="XTypedObject"/> types that must not be abstract.
        /// Xml enlement names are produced by the <paramref name="namer"/>.
        /// Default "namer" is <see cref="AutoNamesFromType"/>.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="types">Set of type to register.</param>
        /// <param name="namer">Functions that associates one or more element names to a type.</param>
        /// <param name="throwOnConflict">False to ignore name conflicts.</param>
        public void RegisterNames( IActivityMonitor monitor, IEnumerable<Type> types, Func<Type, IEnumerable<XName>> namer, bool throwOnConflict = true )
        {
            if( types == null ) throw new ArgumentNullException( nameof( types ) );
            if( namer == null ) throw new ArgumentNullException( nameof( namer ) );
            foreach( var t in types )
            {
                var n = namer( t );
                if( n != null )
                {
                    foreach( var name in n )
                    {
                        if( name != null ) RegisterName( monitor, name, t, throwOnConflict );
                    }
                }
            }
        }

        public void AutoRegisterFromLoadedAssemblies( IActivityMonitor monitor )
        {
            AutoRegisterFromdAssemblies( monitor, AppDomain.CurrentDomain.GetAssemblies() );
        }

        public void AutoRegisterFromdAssemblies( IActivityMonitor monitor, IEnumerable<Assembly> a )
        {
            foreach( var one in a ) AutoRegisterFromdAssembly( monitor, one );
        }

        public void AutoRegisterFromdAssembly( IActivityMonitor monitor, Assembly a )
        {
            if( !a.IsDynamic )
            {
                var allTypes = a.ExportedTypes
                            .Where( t => !t.IsAbstract && typeof( XTypedObject ).IsAssignableFrom( t ) );
                RegisterNames( monitor, allTypes, AutoNamesFromType );
            }
        }

        public IEnumerable<XName> AutoNamesFromType( Type t )
        {
            var a = Attribute.GetCustomAttributes( t, typeof( XNameAttribute ), inherit: false );
            if( a.Length > 0 )
            {
                return a.Cast<XNameAttribute>().Select( x => x.Name );
            }
            return new XName[] { t.Name[0] == 'X' ? t.Name.Substring( 1 ) : t.Name };
        }

        bool DoRegister( IActivityMonitor monitor, XName n, Type t, bool throwOnConflict )
        {
            if( _typeRegister.TryGetValue( n, out var exists ) )
            {
                if( exists != t )
                {
                    var msg = $"Cannot register name '{n}' mapping to type '{t}' since it is already mapped to '{exists}'.";
                    if( throwOnConflict )
                    {
                        throw new ArgumentException( msg );
                    }
                    monitor.Warn( msg );
                    return false;
                }
            }
            else
            {
                monitor.Info( $"Element '{n}' mapped to '{t}'." );
                _typeRegister.Add( n, t );
            }
            return true;
        }

        public Type? GetNameMappping( XName n )
        {
            if( !_typeRegister.TryGetValue( n, out var t ) && _base != null )
            {
                t = _base.GetNameMappping( n );
            }
            return t;
        }

        public Type? GetMappping( XElementReader r )
        {
            var t = GetNameMappping( r.Element.Name );
            if( t == null ) r.WarnUnhandledElements();
            else r.Handle( r.Element );
            return t;
        }

        public T? CreateInstance<T>( IActivityMonitor monitor, XElement e, IServiceProvider? baseProvider = null ) where T : XTypedObject
        {
            return (T?)CreateInstance( monitor, e, baseProvider, typeof( T ) );
        }

        public XTypedObject? CreateInstance( IActivityMonitor monitor, XElement e, IServiceProvider? baseProvider = null, Type? type = null )
        {
            using( monitor.OpenDebug( $"Creating XTypedObject from root {e.ToStringPath()}." ) )
            {
                if( e == null ) throw new ArgumentNullException( nameof( e ) );
                if( monitor == null ) throw new ArgumentNullException( nameof( monitor ) );
                if( _typeRegister.Count == 0 ) AutoRegisterFromLoadedAssemblies( monitor );

                e.Changing += PreventAnyChangesToXElement;
                var eReader = new XElementReader( monitor, e, new HashSet<XObject>() );

                XTypedObject? result = null;
                if( type == null ) type = GetMappping( eReader );
                if( type != null )
                {
                    var rootConfig = new XTypedObject.Initializer( this, eReader, baseProvider );
                    var root = (XTypedObject?)baseProvider?.SimpleObjectCreate( monitor, type, rootConfig );
                    result = root != null && CreateChildren( root, rootConfig ) ? root : null;
                    if( result != null ) eReader.WarnUnhandledAttributes();
                }
                return result;
            }
        }

        static void PreventAnyChangesToXElement( object? sender, XObjectChangeEventArgs e )
        {
            throw new InvalidOperationException( "An XElement that is bound to a TypedObject must not be changed." );
        }

        static bool CreateChildren( XTypedObject parent, XTypedObject.Initializer parentConfig )
        {
            SimpleServiceContainer? cChild = null;
            List<XTypedObject>? created = null;
            XTypedFactory? typeFactory = null;
            var eParent = parent.XElement;
            foreach( var child in eParent.Elements() )
            {
                if( typeFactory == null ) typeFactory = parentConfig.ChildServices.GetService<XTypedFactory>( true );
                var rChild = parentConfig.Reader.WithElement( child, false );
                var tChild = typeFactory.GetMappping( rChild );
                if( tChild != null )
                {
                    if( cChild == null ) cChild = new SimpleServiceContainer( parentConfig.ChildServices );
                    var config = new XTypedObject.Initializer( parent, rChild, cChild );
                    var o = (XTypedObject)cChild.SimpleObjectCreate( rChild.Monitor, tChild, config );
                    if( created == null ) created = new List<XTypedObject>();
                    created.Add( o );
                    if( o == null || !CreateChildren( o, config ) ) return false;
                    rChild.WarnUnhandledAttributes();
                }
            }
            return parent.OnChildrenCreated( parentConfig, (IReadOnlyList<XTypedObject>?)created ?? Array.Empty<XTypedObject>() );
        }

        /// <summary>
        /// Enacapsulates <see cref="Errors"/> and <see cref="Result"/> of <see cref="PreProcess(IActivityMonitor, XElement)"/>.
        /// </summary>
        public struct PreProcessResult
        {
            internal PreProcessResult( IReadOnlyList<ActivityMonitorSimpleCollector.Entry>? errors, XElement result )
            {
                Errors = errors ?? Array.Empty<ActivityMonitorSimpleCollector.Entry>();
                Result = errors != null ? null : result;
            }

            /// <summary>
            /// Gets the preprocessed result.
            /// This can be available even if there are errors.
            /// </summary>
            public XElement? Result { get; }

            /// <summary>
            /// Gets the potential errors or warnings.
            /// </summary>
            public IReadOnlyList<ActivityMonitorSimpleCollector.Entry> Errors { get; }
        }

        public static PreProcessResult PreProcess( IActivityMonitor monitor, XElement e )
        {
            IReadOnlyList<ActivityMonitorSimpleCollector.Entry>? errors = null;
            XElement result;
            using( monitor.CollectEntries( err => errors = err ) )
            {
                result = (XElement)RemoveRegionsAndResolveReusables( new Reusables( monitor, e ) ).Single();
            }
            return new PreProcessResult( errors, result );
        }

        class Reusables
        {
            Dictionary<string, List<XNode>>? _map;

            public Reusables( IActivityMonitor monitor, XElement root )
            {
                Element = root;
                Monitor = monitor;
            }

            public Reusables( Reusables directParent, XElement e )
            {
                Element = e;
                Monitor = directParent.Monitor;
                Parent = directParent;
                while( Parent?.Element.Name == "Region" ) Parent = Parent.Parent;
            }

            public Reusables? Parent;

            public readonly XElement Element;

            public readonly IActivityMonitor Monitor;

            public void Add( string name, List<XNode> e, bool replace, bool @override )
            {
                Monitor.Trace( $"Registering {name} reusable for {Element.ToStringPath()}." );
                if( _map == null ) _map = new Dictionary<string, List<XNode>>();
                bool existsAbove = Parent?.Find( name, clone: false ) != null;
                bool existsHere = _map.ContainsKey( name );
                if( replace && !existsHere )
                {
                    Monitor.Warn( $"{Element.ToStringPath()}: Reusable '{name}' does not replace any previously registered item. Replace=\"True\" attribute should be removed." );
                }
                if( !replace && existsHere )
                {
                    Monitor.Error( $"{Element.ToStringPath()}: Reusable '{name}' is already registered at this level. Use Replace=\"True\" attribute if replacement is intentional." );
                }
                if( @override && !existsAbove )
                {
                    Monitor.Warn( $"{Element.ToStringPath()}: Reusable '{name}' does not override any registered item above. Override=\"True\" attribute should be removed." );
                }
                if( !@override && existsAbove )
                {
                    Monitor.Error( $"{Element.ToStringPath()}: Reusable '{name}' is already registered above. Use Override=\"True\" attribute if redefinition is intentional." );
                }
                _map[name] = e;
            }

            internal IEnumerable<XNode> Apply( XElement e )
            {
                using( Monitor.OpenDebug( $"Applying reusables to {e.ToStringPath()}." ) )
                {
                    if( e.Name == "Reuse" )
                    {
                        if( e.Elements().Any( c => c.Name != "Remove" ) )
                        {
                            Monitor.Error( $"Reuse element {e.ToStringPath()} can not have children other than Remove." );
                            return Array.Empty<XElement>();
                        }
                        string reusableName = (string)e.AttributeRequired( "Name" );
                        IEnumerable<XNode>? reusable = Find( reusableName, clone: true );
                        if( reusable == null )
                        {
                            Monitor.Error( $"Unable to find reusable named '{reusableName}' from {e.ToStringPath()}." );
                            return Array.Empty<XElement>();
                        }
                        Debug.Assert( reusable.OfType<XElement>().DescendantsAndSelf().Any( c => c.Name == "Reuse" ) == false );
                        Monitor.Debug( $"Expanded reusable named '{reusableName}'." );

                        var reusedRoot = new XElement( e.Name, reusable );
                        var removeExpr = e.Elements().Select( r => (string)r.AttributeRequired( "Target" ) ).ToList();
                        foreach( var toRemove in removeExpr )
                        {
                            var removes = reusedRoot.XPathSelectElements( toRemove ).ToList();
                            if( removes.Count == 0 )
                            {
                                Monitor.Error( $"No match found for Remove Target {toRemove} in Reuse {e.ToStringPath()}." );
                                return Array.Empty<XElement>();
                            }
                            foreach( var r in removes ) r.Remove();
                        }
                        return reusedRoot.Nodes();
                    }
                    var children = e.Nodes().SelectMany( n => n is XElement c ? Apply( c ) : new[] { n.Clone() } );
                    return e.Name == "Reusable"
                                ? children
                                : new[] { new XElement(
                                                e.Name,
                                                e.Attributes().Select( a => a.Clone() ),
                                                children ).SetLineColumnInfo( e ) };
                }
            }

            IReadOnlyList<XNode>? Find( string name, bool clone )
            {
                Reusables? c = this;
                do
                {
                    if( c._map != null && c._map.TryGetValue( name, out var reusable ) )
                    {
                        if( !clone ) return reusable;
                        var cloned = new XNode[reusable.Count];
                        for( int i = 0; i < cloned.Length; ++i ) cloned[i] = reusable[i].Clone();
                        return cloned;
                    }
                }
                while( (c = c.Parent) != null );
                return null;
            }
        }

        static IEnumerable<XNode> RemoveRegionsAndResolveReusables( Reusables r )
        {
            var e = r.Element;
            using( r.Monitor.OpenDebug( $"Processing {e.ToStringPath()}." ) )
            {
                if( e.Name == "Reuse" ) return r.Apply( e );
                var children = e.Nodes()
                                .SelectMany( n => n is XElement c ? RemoveRegionsAndResolveReusables( new Reusables( r, c ) ) : new[] { n } );
                if( e.Name == "Region" ) return children;
                if( e.Name == "Reusable" )
                {
                    bool replace = (bool?)e.Attribute( "Replace" ) ?? false;
                    bool @override = (bool?)e.Attribute( "Override" ) ?? false;
                    r.Parent.Add( (string)e.AttributeRequired( "Name" ), children.ToList(), replace, @override );
                    return Enumerable.Empty<XElement>();
                }
                var attr = e.Attributes().Select( a => new XAttribute( a ).SetLineColumnInfo( a ) );
                return new[] { new XElement( e.Name, attr, children ).SetLineColumnInfo( e ) };
            }
        }

    }
}
