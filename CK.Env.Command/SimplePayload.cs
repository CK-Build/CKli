using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace CK.Env
{
    public class SimplePayload
    {
        public class Field
        {
            object _value;

            public string Name { get; }
            public Type Type { get; }
            public bool HasDefaultValue { get; }
            public object DefaultValue { get; }
            public bool IsPassword { get; }

            public bool IsValueSet { get; private set; }

            public void SetValue( object value )
            {
                IsValueSet = true;
                _value = value;
            }

            /// <summary>
            /// Gets the value (the <see cref="DefaultValue"/> if <see cref="IsValueSet"/> is false and <see cref="HasDefaultValue"/> is true)
            /// or <see cref="Type.Missing"/> if there is noi value set nor default.
            /// </summary>
            /// <returns>The value or the missing type marker.</returns>
            public object GetValue() => IsValueSet ? _value : Type.Missing;

            public Field( string name, Type type, bool hasDefaultValue, object defaultValue, bool isPassword = false )
            {
                Name = name;
                Type = type;
                HasDefaultValue = hasDefaultValue;
                DefaultValue = defaultValue;
                IsPassword = isPassword;
            }

            public string RequirementAndName => $"[{(!HasDefaultValue ? "required" : $"default value: {DefaultValue ?? "<null>"}")}] - {Name}";

            public string ValueAndStatus => IsValueSet
                                            ? $"Value = '{GetValue()}' "
                                            : (HasDefaultValue ? $"<use default value: {DefaultValue ?? "<null>"}>" : "<Missing value>");

            public override string ToString() => RequirementAndName + " - " + ValueAndStatus;
        }

        public SimplePayload( IEnumerable<ParameterInfo> parameters )
        {
            Fields = parameters.Select( p => new Field( p.Name, p.ParameterType, p.HasDefaultValue, p.HasDefaultValue ? p.DefaultValue : null ) ).ToArray();
        }

        public IReadOnlyList<Field> Fields { get; }

    }
}
