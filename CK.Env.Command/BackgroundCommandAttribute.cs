using System;

namespace CK.Env
{
    [AttributeUsage(AttributeTargets.Method)]
    public class BackgroundCommandAttribute : Attribute
    {

        /// <summary>
        /// 
        /// </summary>
        /// <param name="always">If true the command is runnable in background. If false, it should be asked to the user.</param>
        public BackgroundCommandAttribute(bool always = true)
        {
            AlwaysRunInBackground = always;
        }
        /// <summary>
        /// Gets if the command should be always launched in background. If not it should be asked to the user.
        /// </summary>
        public bool AlwaysRunInBackground { get;}
    }
}
