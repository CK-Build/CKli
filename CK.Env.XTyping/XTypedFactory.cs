using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CK.Env
{
    public class XTypedFactory
    {
        readonly XTypedFactory _base;
        readonly Dictionary<XName, Type> _typeRegister;

        public XTypedFactory( XTypedFactory baseFactory = null )
        {
            _base = baseFactory;
            _typeRegister = new Dictionary<XName, Type>();
        }

        public bool RegisterName( XName n, Type t, bool throwOnConflict = true )
        {
            if( n == null ) throw new ArgumentNullException( nameof( n ) );
            if( t == null ) throw new ArgumentNullException( nameof( t ) );
            return DoRegister( n, t, throwOnConflict );
        }

        public void RegisterNames( IEnumerable<Type> types, Func<Type, IEnumerable<XName>> namer, bool throwOnConflict = true )
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
                        if( name != null ) RegisterName( name, t, throwOnConflict );
                    }
                }
            }
        }

        public void AutoRegisterFromLoadedAssemblies()
        {
            var allTypes = AppDomain.CurrentDomain.GetAssemblies()
                                .SelectMany( a => a.ExportedTypes )
                                .Where( t => !t.IsAbstract && typeof( XTypedObject ).IsAssignableFrom( t ) );
            RegisterNames( allTypes, AutoNamesFromType );
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

        bool DoRegister( XName n, Type t, bool throwOnConflict )
        {
            if( _typeRegister.TryGetValue( n, out var exists ) )
            {
                if( exists != t )
                {
                    if( throwOnConflict )
                    {
                        throw new ArgumentException( $"Cannot register name '{n}' mapping to type '{t}' since it is already mapped to '{exists}'." );
                    }
                    return false;
                }
            }
            else _typeRegister.Add( n, t );
            return true;
        }

        public Type GetNameMappping( XName n )
        {
            if( !_typeRegister.TryGetValue( n, out var t ) && _base != null )
            {
                t = _base.GetNameMappping( n );
            }
            return t;
        }

        public Type GetMappping( XElement e ) => GetNameMappping( e.Name );

        public T CreateInstance<T>( IActivityMonitor monitor, XElement e, IServiceProvider baseProvider = null ) where T : XTypedObject
        {
            return (T)CreateInstance( monitor, e, baseProvider, typeof( T ) );
        }

        public XTypedObject CreateInstance( IActivityMonitor monitor, XElement e, IServiceProvider baseProvider = null, Type type = null )
        {
            if( monitor == null ) throw new ArgumentNullException( nameof( monitor ) );
            if( _typeRegister.Count == 0 ) AutoRegisterFromLoadedAssemblies();

            var typedRoot = TypedXml.Create( e, GetMappping, type );
            var rootConfig = new XTypedObject.Initializer( monitor, typedRoot, baseProvider );
            var root = (XTypedObject)baseProvider.SimpleObjectCreate( monitor, typedRoot.Type, rootConfig );
            return root != null && CreateChildren( root, rootConfig ) ? root : null;
        }

        static bool CreateChildren( XTypedObject parent, XTypedObject.Initializer parentConfig )
        {
            SimpleServiceContainer cChild = new SimpleServiceContainer( parentConfig.ChildServices );
            int count = parentConfig.TypedXml.TypedChildren.Count;
            var created = new XTypedObject[count];
            for( int i = 0; i < count; ++i )
            {
                var c = parentConfig.TypedXml.TypedChildren[i];
                var config = new XTypedObject.Initializer( parentConfig.Monitor, parent, c, cChild );
                var o = created[i] = (XTypedObject)cChild.SimpleObjectCreate( parentConfig.Monitor, c.Type, config );
                if( o == null || !CreateChildren( o, config ) ) return false;
            }
            parent.OnChildrenCreated( parentConfig, created );
            return true;
        }


        static public XElement PreProcess( IActivityMonitor monitor, XElement e )
        {
            var clone = RemoveRegionsAndResolveReusables( new Reusables( monitor, e ) ).Single();
            return clone;
        }

        class Reusables
        {
            Dictionary<string, List<XElement>> _map;

            public Reusables( IActivityMonitor monitor, XElement root )
            {
                Element = root;
                Monitor = monitor;
            }

            public Reusables( Reusables directParent, XElement e )
            {
                Element = e;
                Monitor = directParent.Monitor;
                while( directParent.Element.Name == "Region" )
                {
                    directParent = directParent.Parent;
                }
                Parent = directParent;
            }

            public Reusables Parent;

            public readonly XElement Element;

            public readonly IActivityMonitor Monitor;

            public void Add( string name, List<XElement> e, bool replace, bool @override )
            {
                Monitor.Trace( $"Registering {name} reusable for {Element.ToStringPath()}." );
                if( _map == null ) _map = new Dictionary<string, List<XElement>>();
                bool existsAbove = Parent?.Find( name ) != null;
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

            internal IEnumerable<XElement> Apply( XElement e )
            {
                using( Monitor.OpenDebug( $"Applying reusables to {e.ToStringPath()}." ) )
                {
                    if( e.Name == "Reuse" )
                    {
                        if( e.Elements().Any() )
                        {
                            Monitor.Error( $"Reuse element {e.ToStringPath()} can not have children." );
                            return Array.Empty<XElement>();
                        }
                        string reusableName = (string)e.AttributeRequired( "Name" );
                        IEnumerable<XElement> reusable = Find( reusableName );
                        if( reusable == null )
                        {
                            Monitor.Error( $"Unable to find reusable named '{reusableName}' from {e.ToStringPath()}." );
                            return Array.Empty<XElement>();
                        }
                        Debug.Assert( reusable.DescendantsAndSelf().Any( c => c.Name == "Reuse" ) == false );
                        Monitor.Debug( $"Expanded reusable named '{reusableName}'." );
                        return reusable.Select( c => new XElement( c ) );
                    }
                    var children = e.Elements().SelectMany( c => Apply( c ) );
                    return e.Name == "Reusable"
                                ? children
                                : new[] { new XElement( e.Name, e.Attributes(), children ) };
                }
            }

            IEnumerable<XElement> Find( string name )
            {
                var c = this;
                do
                {
                    if( c._map != null && c._map.TryGetValue( name, out var reusable ) ) return reusable;
                }
                while( (c = c.Parent) != null );
                return null;
            }
        }

        static IEnumerable<XElement> RemoveRegionsAndResolveReusables( Reusables r )
        {
            var e = r.Element;
            using( r.Monitor.OpenDebug( $"Processing {e.ToStringPath()}." ) )
            {
                if( e.Name == "Reuse" ) return r.Apply( e );
                var children = e.Elements()
                                .SelectMany( c => RemoveRegionsAndResolveReusables( new Reusables( r, c ) ) );
                if( e.Name == "Region" ) return children;
                if( e.Name == "Reusable" )
                {
                    bool replace = (bool?)e.Attribute( "Replace" ) ?? false;
                    bool @override = (bool?)e.Attribute( "Override" ) ?? false;
                    r.Parent.Add( (string)e.AttributeRequired( "Name" ), children.ToList(), replace, @override );
                    return Enumerable.Empty<XElement>();
                }
                return new[] { new XElement( e.Name, e.Attributes(), children ) };
            }
        }
    }
}
