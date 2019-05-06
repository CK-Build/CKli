
namespace CK.Env.MSBuildSln
{
    public class PropertyLine
    {
        public PropertyLine( string name, string value, int lineNumber = 0 )
        {
            Name = name;
            Value = value;
            LineNumber = LineNumber;
        }

        /// <summary>
        /// Gets the property name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets or sets the property value.
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// Gets the line number. 0 for an unknown line number.
        /// </summary>
        public int LineNumber { get; set; }

        public override string ToString() => $"{Name} = {Value}";

    }
}
