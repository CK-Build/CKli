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

        /// <summary>
        /// Gets or sets the <see cref="ParallelCommandMode"/> for this command.
        /// Defaults to <see cref="ParallelCommandMode.Sequential"/>.
        /// </summary>
        public ParallelCommandMode ParallelMode { get; set; }
    }
}
