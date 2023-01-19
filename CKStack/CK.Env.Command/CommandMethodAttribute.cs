using System;

namespace CK.Env
{
    [AttributeUsage( AttributeTargets.Method )]
    public class CommandMethodAttribute : Attribute
    {
        public CommandMethodAttribute( bool confirmationRequired = true )
        {
            ConfirmationRequired = confirmationRequired;
        }

        public bool ConfirmationRequired { get; }
    }
}
