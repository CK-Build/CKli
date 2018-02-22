using CKli;
using CK.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env.Analysis
{
    public abstract class XAction : XTypedObject
    {
        readonly List<IParameter> _parameters;

        protected XAction( Initializer initializer, ActionCollector collector )
            : base( initializer )
        {
            _parameters = new List<IParameter>();
            Number = collector.Add( this );
            if( Title == null ) Title = XElement.Name.LocalName;
        }

        public int Number { get; }

        public string Title { get; protected set; }

        public IReadOnlyList<IParameter> Parameters => _parameters;

        public interface IParameter
        {
            string Name { get; }

            bool HasDefaultValue { get; }

            object DefaultValue { get; }

            Type ParameterType { get; }

            bool HasValue { get; }

            object Value { get; }

            bool ParseAndSet( IActivityMonitor m, string input );

        }

        public interface IParameter<T> : IParameter
        {
            new T DefaultValue { get; }

            bool Set( IActivityMonitor m, T value );

            new T Value { get; }
        }

        public abstract class Parameter<T> : IParameter<T>
        {
            readonly string _name;
            readonly bool _hasDefaultValue;
            readonly T _defaultValue;
            readonly Func<IActivityMonitor, T, bool> _validator;
            bool _isSet;
            T _value;

            internal Parameter( string name, bool hasDefaultvalue, T defaultValue, Func<IActivityMonitor,T,bool> validator )
            {
                _name = name;
                _hasDefaultValue = hasDefaultvalue;
                _defaultValue = defaultValue;
                _validator = validator;
            }

            public string Name => _name;

            public bool HasDefaultValue => _hasDefaultValue;

            public T DefaultValue => _defaultValue;

            object IParameter.DefaultValue => _defaultValue;

            object IParameter.Value => Value;

            public Type ParameterType => typeof(T);

            public bool HasValue => _isSet || _hasDefaultValue;

            public T Value
            {
                get
                {
                    if( _isSet ) return _value;
                    if( _hasDefaultValue ) return _defaultValue;
                    throw new InvalidOperationException( "Parameter value has not been set and has no default value." );
                }
            }

            public bool Set( IActivityMonitor monitor, T value )
            {
                if( _validator?.Invoke( monitor, value ) ?? true )
                {
                    _value = value;
                    return _isSet = true;
                }
                return false;
            }

            protected abstract bool TryParse( IActivityMonitor m, string input, out T value );

            public bool ParseAndSet( IActivityMonitor monitor, string input )
            {
                if( monitor == null ) throw new ArgumentNullException( nameof( monitor ) );
                if( input == null ) throw new ArgumentNullException( nameof( input ) );
                return TryParse( monitor, input, out T val ) && Set( monitor, val );
           }
        }

        public class StringParameter : Parameter<string>
        {
            internal StringParameter( string name, string defaultValue, Func<IActivityMonitor, string, bool> validator )
                : base( name, defaultValue != null, defaultValue, validator )
            {
            }

            protected override bool TryParse( IActivityMonitor m, string input, out string value )
            {
                value = input;
                return true;
            }
        }

        protected StringParameter AddStringParameter( string name, Func<IActivityMonitor, string, bool> validator )
        {
            return AddStringParameter( name, null, validator );
        }

        protected StringParameter AddStringParameter( string name, string defaultValue = null, Func<IActivityMonitor, string, bool> validator = null )
        {
            var p = new StringParameter( name, defaultValue, validator );
            _parameters.Add( p );
            return p;
        }

        public abstract bool Run( IActivityMonitor monitor );

        public override string ToString() => $"{Number} - {Title}";
    }
}
