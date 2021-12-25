using CK.Core;
using System;
using System.Diagnostics;

namespace CK.Env.MSBuildSln
{
    public class PartialEvaluator : Visitor
    {
        bool? _currentResult;

        /// <summary>
        /// Returns null on parse or evaluation error: consider unknown result (a null boolean return by the
        /// actual evaluation of <see cref="PartialEvaluation(BaseNode, Func{string, string})"/>) to be true.
        /// </summary>
        /// <param name="m">The monitor (will receive parse and evaluation error).</param>
        /// <param name="condition">The condition to parse. Can be null or empty.</param>
        /// <param name="properties">Optional known $(property) mappings.</param>
        /// <returns>True or false when no error. Null on error.</returns>
        public bool? EvalFinalResult( IActivityMonitor m, string condition, Func<string, string>? properties = null )
        {
            if( !MSBuildConditionParser.TryParse( m, condition, out BaseNode? node ) ) return null;
            try
            {
                return PartialEvaluation( node, properties ) ?? true;
            }
            catch( Exception ex )
            {
                m.Error( $"While parsing Condition = '{condition}'.", ex );
                return null;
            }
        }

        /// <summary>
        /// Evaluates the MSBuild condition string and returns null if one can not
        /// conclude between true and false (because of unavailable $(property) values.
        /// </summary>
        /// <param name="node">The root node to evaluate. The empty node is null and always evaluates to true.</param>
        /// <param name="properties">Optional known $(property) mappings.</param>
        /// <returns>True, False or null if one can not conclude.</returns>
        public bool? PartialEvaluation( BaseNode? node, Func<string, string>? properties = null )
        {
            if( node == null ) return true;
            if( properties != null )
            {
                var replacer = new PropertyReplacer( properties );
                node = replacer.Visit( node );
            }
            _currentResult = null;
            Visit( node );
            return _currentResult;
        }

        protected override BaseNode VisitLogicalConnector( BinaryOperatorNode node )
        {
            Visit( node.Left );
            if( _currentResult == null
                || (_currentResult.Value != (node.Operator == TokenType.Or)) )
            {
                Visit( node.Right );
            }
            return node;
        }

        protected override BaseNode VisitNot( NotNode node )
        {
            Visit( node.Left );
            if( _currentResult.HasValue ) _currentResult = !_currentResult.Value;
            return node;
        }

        protected override BaseNode VisitFunction( FunctionCallNode node )
        {
            _currentResult = null;
            return node;
        }

        protected override BaseNode VisitComparison( BinaryOperatorNode node )
        {
            if( node.Operator.IsOrderingOperator() )
            {
                NumericNode nL = node.Left.AsNumeric;
                NumericNode nR = node.Right.AsNumeric;
                if( nL == null || nR == null )
                {
                    throw new Exception( $"{node}: unable to resolve numeric operand." );
                }
                switch( node.Operator )
                {
                    case TokenType.GreaterThan:
                        _currentResult = nL.DoubleValue > nR.DoubleValue;
                        return node;
                    case TokenType.GreaterOrEqualTo:
                        _currentResult = nL.DoubleValue >= nR.DoubleValue;
                        return node;
                    case TokenType.LessThan:
                        _currentResult = nL.DoubleValue < nR.DoubleValue;
                        return node;
                }
                Debug.Assert( node.Operator == TokenType.LessOrEqualTo );
                _currentResult = nL.DoubleValue <= nR.DoubleValue;
                return node;
            }

            Debug.Assert( node.Operator == TokenType.EqualTo || node.Operator == TokenType.NotEqualTo );
            Visit( node.Left );
            bool? left = _currentResult;
            Visit( node.Right );
            bool? right = _currentResult;
            if( left.HasValue && right.HasValue )
            {
                // Easy: two known booleans.
                _currentResult = (left.Value == right.Value) == (node.Operator == TokenType.EqualTo);
                return node;
            }
            if( left.HasValue || right.HasValue )
            {
                // One known boolean and ???
                BaseNode other = left.HasValue ? node.Right : node.Left;
                // If it is a terminal, it is always different than a boolean: we can conclude.
                // Otherwise, it is an unknown result: we conclude that it is still unknown.
                if( other.IsTerminal ) _currentResult = node.Operator == TokenType.NotEqualTo;
                else _currentResult = null;
                return node;
            }
            // No resolved booleans. Try Numeric first and then string.
            var numL = node.Left.AsNumeric;
            var numR = node.Right.AsNumeric;
            if( numL != null && numR != null )
            {
                _currentResult = (numL.DoubleValue == numR.DoubleValue) == (node.Operator == TokenType.EqualTo);
                return node;
            }
            // If they are equal, we consider that any expansion will lead to equality.
            bool areEqual = node.Left.StringValue.Equals( node.Right.StringValue, StringComparison.OrdinalIgnoreCase );
            if( areEqual )
            {
                _currentResult = node.Operator == TokenType.EqualTo;
                return node;
            }
            // But, if they differ, we can conclude ONLY if no expansions are required.
            if( !node.Left.RequiresExpansion && !node.Right.RequiresExpansion )
            {
                _currentResult = node.Operator == TokenType.NotEqualTo;
                return node;
            }
            // This is an unknown result.
            _currentResult = null;
            return node;
        }

        protected override BaseNode VisitNumeric( NumericNode node )
        {
            Debug.Assert( node.AsBoolean == null );
            _currentResult = null;
            return node;
        }

        protected override BaseNode VisitString( StringNode node )
        {
            _currentResult = node.AsBoolean;
            return node;
        }

    }
}
