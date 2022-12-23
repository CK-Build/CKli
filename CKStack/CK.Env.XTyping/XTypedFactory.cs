using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace CK.Env
{
    public class XTypedFactory
    {
        readonly XTypedFactory? _base;
        readonly Dictionary<XName, Type> _typeRegister;
        readonly HashSet<Assembly> _already;
        bool _isLocked;

        public XTypedFactory( XTypedFactory? baseFactory = null )
        {
            _base = baseFactory;
            _typeRegister = new Dictionary<XName, Type>();
            _already = new HashSet<Assembly>();
        }

        /// <summary>
        /// Gets whether this factory does not allow any new registrations.
        /// It can only be used to create instances based on existing registrations.
        /// </summary>
        public bool IsLocked => _isLocked;

        /// <summary>
        /// Sets <see cref="IsLocked"/> to true.
        /// </summary>
        public void SetLocked() => _isLocked = true;

        /// <summary>
        /// Explicit name to type registration.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="n">The element name.</param>
        /// <param name="t">The type of the associated object.</param>
        /// <param name="throwOnConflict">False to ignore name conflicts.</param>
        /// <returns>True on success, false on name conflict.</returns>
        public bool RegisterName( IActivityMonitor monitor, XName n, Type t, bool throwOnConflict = true )
        {
            Throw.CheckNotNullArgument( n );
            Throw.CheckNotNullArgument( t );
            return DoRegister( monitor, n, t, throwOnConflict );
        }

        /// <summary>
        /// Registers a set of <see cref="XTypedObject"/> types that must not be abstract.
        /// Xml element names are produced by the <paramref name="namer"/>.
        /// Default "namer" is <see cref="AutoNamesFromType"/>.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="types">Set of type to register.</param>
        /// <param name="namer">Functions that associates one or more element names to a type.</param>
        /// <param name="throwOnConflict">False to ignore name conflicts.</param>
        public void RegisterNames( IActivityMonitor monitor, IEnumerable<Type> types, Func<Type, IEnumerable<XName>> namer, bool throwOnConflict = true )
        {
            Throw.CheckNotNullArgument( types );
            Throw.CheckNotNullArgument( namer );
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

        /// <summary>
        /// Gets whether the assembly has already been registered in this factory
        /// or a base one.
        /// </summary>
        /// <param name="a">The assembly to challenge.</param>
        /// <returns>True if it has already been registered. False otherwise.</returns>
        public bool HasAlreadyRegistered( Assembly a ) => _already.Contains( a ) || (_base?.HasAlreadyRegistered( a ) ?? false);

        public void AutoRegisterFromLoadedAssemblies( IActivityMonitor monitor )
        {
            AutoRegisterFromAssemblies( monitor, AppDomain.CurrentDomain.GetAssemblies() );
        }

        public void AutoRegisterFromAssemblies( IActivityMonitor monitor, IEnumerable<Assembly> a )
        {
            foreach( var one in a ) AutoRegisterFromAssembly( monitor, one );
        }

        /// <summary>
        /// Registers all public non abstract <see cref="XTypedObject"/> in an assembly.
        /// </summary>
        /// <param name="monitor">The monitor.</param>
        /// <param name="a">The assembly to register.</param>
        public void AutoRegisterFromAssembly( IActivityMonitor monitor, Assembly a )
        {
            CheckLocked();
            // Always add the assembly in the local already processed.
            if( !a.IsDynamic && _already.Add( a ) && (_base == null || !_base.HasAlreadyRegistered( a )) )
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
            CheckLocked();
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

        void CheckLocked()
        {
            if( _isLocked ) throw new InvalidOperationException( "Locked XTypedFactory cannot register new types." );
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
                    var o = (XTypedObject?)cChild.SimpleObjectCreate( rChild.Monitor, tChild, config );
                    if( created == null ) created = new List<XTypedObject>();
                    if( o == null || !CreateChildren( o, config ) ) return false;
                    created.Add( o );
                    rChild.WarnUnhandledAttributes();
                }
            }
            return parent.OnChildrenCreated( parentConfig, (IReadOnlyList<XTypedObject>?)created ?? Array.Empty<XTypedObject>() );
        }

        /// <summary>
        /// Encapsulates <see cref="Errors"/> and <see cref="Result"/> of <see cref="PreProcess(IActivityMonitor, XElement)"/>.
        /// </summary>
        public readonly struct PreProcessResult
        {
            internal PreProcessResult( IReadOnlyList<ActivityMonitorSimpleCollector.Entry> errors, XElement result )
            {
                Errors = errors;
                Result = errors.Count > 0 ? null : result;
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
            XElement result;
            using( monitor.CollectEntries( out var errors ) )
            {
                result = (XElement)RemoveRegionsAndResolveReusables( monitor, new ReusableWrapper( e ) ).Single();
                return new PreProcessResult( errors, result );
            }
        }

        class ReusableWrapper
        {
            Dictionary<string, List<XNode>>? _map;

            /// <summary>
            /// Initializes a root <see cref="ReusableWrapper"/>.
            /// </summary>
            /// <param name="root">The preprocess root (may not be the document root).</param>
            public ReusableWrapper( XElement root )
            {
                Element = root;
            }

            /// <summary>
            /// Initializes a <see cref="ReusableWrapper"/> on an element.
            /// Regions are "transparent": <see cref="Parent"/> goes across &lt;Region&gt; elements.
            /// </summary>
            /// <param name="directParent">The parent element (may be a region).</param>
            /// <param name="e">The element to wrap.</param>
            public ReusableWrapper( ReusableWrapper directParent, XElement e )
            {
                Element = e;
                Parent = directParent;
                while( Parent?.Element.Name == "Region" ) Parent = Parent.Parent;
                Debug.Assert( Parent != null );
            }

            /// <summary>
            /// Gets the parent wrapper. Null only for the preprocess root.
            /// </summary>
            public ReusableWrapper? Parent;

            /// <summary>
            /// Gets the wrapped element.
            /// </summary>
            public readonly XElement Element;

            /// <summary>
            /// Registers a &lt;Reusable&gt; <paramref name="children"/> &lt;/Reusable&gt; fragment.
            /// </summary>
            /// <param name="monitor">The monitor to use.</param>
            /// <param name="name">The name of this reusable fragment.</param>
            /// <param name="children">The content of this fragment.</param>
            /// <param name="replace">True if this fragment is supposed to replace an existing fragment in this <see cref="ReusableWrapper"/>.</param>
            /// <param name="override">True if this fragment is supposed to override an existing fragment defined in <see cref="Parent"/>.</param>
            public void Add( IActivityMonitor monitor, string name, List<XNode> children, bool replace, bool @override )
            {
                monitor.Trace( $"Registering {name} reusable for {Element.ToStringPath()}." );
                if( _map == null ) _map = new Dictionary<string, List<XNode>>();
                bool existsAbove = Parent?.Find( name, clone: false ) != null;
                bool existsHere = _map.ContainsKey( name );
                if( replace && !existsHere )
                {
                    monitor.Warn( $"{Element.ToStringPath()}: Reusable '{name}' does not replace any previously registered item. Replace=\"True\" attribute should be removed." );
                }
                if( !replace && existsHere )
                {
                    monitor.Error( $"{Element.ToStringPath()}: Reusable '{name}' is already registered at this level. Use Replace=\"True\" attribute if replacement is intentional." );
                }
                if( @override && !existsAbove )
                {
                    monitor.Warn( $"{Element.ToStringPath()}: Reusable '{name}' does not override any registered item above. Override=\"True\" attribute should be removed." );
                }
                if( !@override && existsAbove )
                {
                    monitor.Error( $"{Element.ToStringPath()}: Reusable '{name}' is already registered above. Use Override=\"True\" attribute if redefinition is intentional." );
                }
                _map[name] = children;
            }

            /// <summary>
            /// Applies any fragment registered by <see cref="Add"/> to an existing element: all &lt;Reuse Name="" &gt; are processed and must be found.
            /// The source element is transformed into a cloned list of Xml nodes.
            /// </summary>
            /// <param name="monitor">The monitor to use.</param>
            /// <param name="e">Source element to transform.</param>
            /// <returns>The set of nodes that replace the source element.</returns>
            internal IEnumerable<XNode> Apply( IActivityMonitor monitor, XElement e )
            {
                using( monitor.OpenDebug( $"Applying reusables to {e.ToStringPath()}." ) )
                {
                    if( e.Name == "Reuse" )
                    {
                        if( e.Elements().Any( c => c.Name != "Remove" ) )
                        {
                            monitor.Error( $"Reuse element {e.ToStringPath()} can not have children other than Remove." );
                            return Array.Empty<XElement>();
                        }
                        string reusableName = (string)e.AttributeRequired( "Name" );
                        IEnumerable<XNode>? reusable = Find( reusableName, clone: true );
                        if( reusable == null )
                        {
                            monitor.Error( $"Unable to find reusable named '{reusableName}' from {e.ToStringPath()}." );
                            return Array.Empty<XElement>();
                        }
                        Debug.Assert( reusable.OfType<XElement>().DescendantsAndSelf().Any( c => c.Name == "Reuse" ) == false );
                        monitor.Debug( $"Expanded reusable named '{reusableName}'." );

                        var reusedRoot = new XElement( e.Name, reusable );
                        var removeExpr = e.Elements().Select( r => (string)r.AttributeRequired( "Target" ) ).ToList();
                        foreach( var toRemove in removeExpr )
                        {
                            var removes = reusedRoot.XPathSelectElements( toRemove ).ToList();
                            if( removes.Count == 0 )
                            {
                                monitor.Error( $"No match found for Remove Target {toRemove} in Reuse {e.ToStringPath()}." );
                                return Array.Empty<XElement>();
                            }
                            foreach( var r in removes ) r.Remove();
                        }
                        return reusedRoot.Nodes();
                    }
                    var children = e.Nodes().SelectMany( n => n is XElement c ? Apply( monitor, c ) : new[] { n.Clone() } );
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
                ReusableWrapper? c = this;
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

        static IEnumerable<XNode> RemoveRegionsAndResolveReusables( IActivityMonitor monitor, ReusableWrapper r )
        {
            var e = r.Element;
            using( monitor.OpenDebug( $"Processing {e.ToStringPath()}." ) )
            {
                if( e.Name == "Reuse" ) return r.Apply( monitor, e );
                var children = e.Nodes()
                                .SelectMany( n => n is XElement c ? RemoveRegionsAndResolveReusables( monitor, new ReusableWrapper( r, c ) ) : new[] { n } );
                if( e.Name == "Region" ) return children;
                if( e.Name == "Reusable" )
                {
                    Debug.Assert( r.Parent != null, "We are necessarily in the root Reusable." );
                    bool replace = (bool?)e.Attribute( "Replace" ) ?? false;
                    bool @override = (bool?)e.Attribute( "Override" ) ?? false;
                    r.Parent.Add( monitor, (string)e.AttributeRequired( "Name" ), children.ToList(), replace, @override );
                    return Enumerable.Empty<XElement>();
                }
                var attr = e.Attributes().Select( a => new XAttribute( a ).SetLineColumnInfo( a ) );
                return new[] { new XElement( e.Name, attr, children ).SetLineColumnInfo( e ) };
            }
        }

    }
}
