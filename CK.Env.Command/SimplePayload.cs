using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    public class SimplePayload 
    {
        public class Field
        {
            object _value;

            public string Name { get; }
            public Type Type { get; }
            public bool IsRequired { get; }
            public bool IsPassword { get; }

            public bool HasValue { get; private set; }

            public void SetValue( object value )
            {
                HasValue = true;
                _value = value;
            }

            /// <summary>
            /// Gets the value if <see cref="HasValue"/> is true or <see cref="Type.Missing"/>.
            /// </summary>
            /// <returns>The value or the missing type marker.</returns>
            public object GetValue() => HasValue  ? _value : Type.Missing;

            public Field( string name, Type type, bool required, bool isPassword = false )
            {
                Name = name;
                Type = type;
                IsRequired = required;
                IsPassword = isPassword;
            }

            public string RequirementAndName => $"[{(IsRequired ? "required" : "optional")}] - {Name}";

            public string ValueAndStatus => HasValue
                                            ? $"Value = '{GetValue()}' "
                                            : (IsRequired ? "<Missing value>" : "<use default value>");

            public override string ToString() => RequirementAndName + " - " + ValueAndStatus;
        }

        public SimplePayload( IEnumerable<Field> f )
        {
            Fields = f.ToArray();
        }

        public SimplePayload( IEnumerable<ParameterInfo> parameters )
        {
            Fields = parameters.Select( p => new Field( p.Name, p.ParameterType, !p.HasDefaultValue ) ).ToArray();
        }

        public IReadOnlyList<Field> Fields { get; }

    }
}
