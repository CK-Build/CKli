using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env.MSBuild
{
    public class Visitor
    {
        public BaseNode Visit( BaseNode node )
        {
            if( node == null ) throw new ArgumentNullException( nameof( node ) );
            switch( node )
            {
                case BinaryOperatorNode comp when comp.Operator.IsComparisonOperator(): return VisitComparison( comp );
                case BinaryOperatorNode andOr: return VisitLogicalConnector( andOr );
                case NotNode n: return VisitNot( n );
                case StringNode n: return VisitString( n );
                case NumericNode n: return VisitNumeric( n );
                case FunctionCallNode f: return VisitFunction( f );
                default: throw new NotSupportedException();
            }
        }

        BaseNode StandardVisitBinary( BinaryOperatorNode node )
        {
            var left = Visit( node.Left );
            var right = Visit( node.Right );
            return left == node.Left && right == node.Right
                    ? node
                    : new BinaryOperatorNode( left, node.Operator, right );
        }

        protected virtual BaseNode VisitComparison( BinaryOperatorNode node )
        {
            return StandardVisitBinary( node );
        }

        protected virtual BaseNode VisitLogicalConnector( BinaryOperatorNode node )
        {
            return StandardVisitBinary( node );
        }

        protected virtual BaseNode VisitNot( NotNode node )
        {
            var left = Visit( node.Left );
            return left == node.Left ? node : new NotNode( left );
        }

        protected virtual BaseNode VisitString( StringNode node )
        {
            return node;
        }

        protected virtual BaseNode VisitNumeric( NumericNode node )
        {
            return node;
        }

        protected virtual BaseNode VisitFunction( FunctionCallNode node )
        {
            List<BaseNode> vArgs = null;
            for( int i = 0; i < node.Arguments.Count; ++i )
            {
                var a = node.Arguments[i];
                var aV = Visit( a );
                if( aV != a )
                {
                    if( vArgs == null ) vArgs = new List<BaseNode>( node.Arguments.Take(i) );
                    if( aV != null ) vArgs.Add( aV );
                }
            }
            return vArgs == null
                    ? node
                    : new FunctionCallNode( node.FunctionName, vArgs );
        }


    }
}
