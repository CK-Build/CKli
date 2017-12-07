using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace CK.Env
{
    public abstract class XTypedObject
    {
        public class Initializer
        {
            Dictionary<object, object> _initializationState;

            internal Initializer( IActivityMonitor monitor, XTypedObject parent, TypedXml e, SimpleServiceContainer services )
            {
                TypedXml = e;
                Parent = parent;
                Monitor = monitor;
                Services = services;
                ChildServices = new SimpleServiceContainer( Services );
            }

            internal Initializer( IActivityMonitor monitor, TypedXml e, IServiceProvider baseProvider )
            {
                TypedXml = e;
                Monitor = monitor;
                Services = new SimpleServiceContainer( baseProvider );
                ChildServices = new SimpleServiceContainer( Services );
            }

            internal TypedXml TypedXml { get; }

            public IActivityMonitor Monitor { get; }

            public XElement Element => TypedXml.Element;

            public XTypedObject Parent { get; }

            public ISimpleServiceContainer Services { get; }

            public ISimpleServiceContainer ChildServices { get; }

            public IDictionary<object, object> InitializationState
            {
                get
                {
                    if( _initializationState == null )
                    {
                        _initializationState = new Dictionary<object, object>();
                    }
                    return _initializationState;
                }
            }

        }

        protected XTypedObject( Initializer initializer )
        {
            var a = initializer.Element.FirstAttribute;
            while( a != null )
            {
                var p = GetType().GetProperty( a.Name.LocalName );
                if( p != null && p.CanWrite )
                {
                    var v = Convert.ChangeType( a.Value, p.PropertyType );
                    p.SetValue( this, v );
                }
                a = a.NextAttribute;
            }
        }

        public IReadOnlyList<XTypedObject> Children { get; private set; }

        protected virtual void OnCreated( Initializer initializer )
        {
        }

        public IEnumerable<T> Descendants<T>()
        {
            foreach( var c in Children )
            {
                if( c is T tC ) yield return tC;
                foreach( var d in c.Descendants<T>() )
                {
                    if( d is T tD ) yield return tD;
                }
            }
        }

        internal void OnChildrenCreated( Initializer initializer, IReadOnlyList<XTypedObject> children )
        {
            Children = children;
            OnCreated( initializer );
        }

    }
}
