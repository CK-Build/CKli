

namespace CK.Env.MSBuildSln
{
    /// <summary>
    /// Base class for nodes.
    /// </summary>
    public abstract class BaseNode
    {
        public virtual NumericNode AsNumeric => null;

        public virtual bool? AsBoolean => null;

        public virtual string StringValue => null;

        public virtual bool RequiresExpansion => false;

        public bool IsTerminal => StringValue == null;

    }
}
