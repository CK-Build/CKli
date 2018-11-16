using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    [AttributeUsage(AttributeTargets.Method)]
    public class CommandMethodAttribute : Attribute
    {
    }
}
