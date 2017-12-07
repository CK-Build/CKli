using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env.Analysis
{
    public class ActionCollector
    {
        readonly List<IEnvAction> _actions;

        class EnvAction : IEnvAction
        {
            readonly Func<IActivityMonitor,bool> _action;

            public EnvAction(
                int n,
                string title,
                Func<IActivityMonitor, bool> a,
                string description)
            {
                Number = n;
                Title = title;
                _action = a;
                Description = description;
            }

            public int Number { get; }

            public string Title { get; }

            public string Description { get; }

            public bool Run( IActivityMonitor monitor )
            {
                return _action( monitor );
            }
        }

        public void Add( string title, Func<IActivityMonitor, bool> a, string description )
        {
            _actions.Add( new EnvAction(_actions.Count, title, a, description));
        }
    }
}
