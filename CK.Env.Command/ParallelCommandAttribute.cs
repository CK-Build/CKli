using System;

namespace CK.Env
{
    [AttributeUsage( AttributeTargets.Method )]
    public class ParallelCommandAttribute : Attribute
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="always">If true the command is always parallelizable. If false, it should be asked to the user.</param>
        public ParallelCommandAttribute( bool always = true )
        {
            AlwaysRunInParallel = always;
        }
        /// <summary>
        /// Gets if the command should be always parralel. If not it should be asked to the user.
        /// </summary>
        public bool AlwaysRunInParallel { get; }

    }
}
