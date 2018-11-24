using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env.World
{
    public interface IWorld
    {
        IWorldName Name { get; }

        GlobalWorkStatus WorkStatus { get; }
    }
}
