using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace Viv.Momo.Core
{
    public class ParameterReplacer : ExpressionVisitor
    {
        private readonly ParameterExpression _newParam;
        public ParameterReplacer(ParameterExpression newParam)
        {
            _newParam = newParam;
        }
        protected override Expression VisitParameter(ParameterExpression node)
        {
            return _newParam;
        }
    }
}
