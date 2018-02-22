using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env.Analysis
{
    public class ActionCollector
    {
        List<XAction> _actions;

        public ActionCollector()
        {
            _actions = new List<XAction>();
        }

        internal int Add( XAction a )
        {
            _actions.Add( a );
            return _actions.Count - 1;
        }

        public IReadOnlyList<XAction> Actions => _actions;

        public void Clear()
        {
            _actions.Clear();
        }
    }
}
