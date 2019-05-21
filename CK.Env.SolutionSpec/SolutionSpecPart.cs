//using System;
//using System.Collections.Generic;
//using System.Text;
//using System.Xml.Linq;

//namespace CK.Env
//{
//    interface ISpecData
//    {
//        XElement Element { get; }

//        ISet<XElement> Processed
//    }

//    /// <summary>
//    /// Base class that supports the definition of a part of <see cref="SharedSolutionSpec"/>
//    /// or <see cref="SolutionSpec"/>.
//    /// </summary>
//    public abstract class SolutionSpecBuilder
//    {
//        List<Func<IActivityMonitor,> _builders;
//        List<SolutionSpecBuilder> _builders;

//        public void Register( SolutionSpecBuilder part )
//        {
//            if( part._owner != null ) throw new InvalidOperationException();
//            part._owner = this;
//            if( _builders == null ) _builders = new List<SolutionSpecBuilder>();
//            _builders.Add( part );
//        }

//        public object Build( XElement e )
//        {

//        }

//        public object Apply( object o, XElement e )
//        {

//        }

//    }
//}
