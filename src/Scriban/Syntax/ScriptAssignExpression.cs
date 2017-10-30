// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license. 
// See license.txt file in the project root for full license information.

using System.IO;

namespace Scriban.Syntax
{
    [ScriptSyntax("assign expression", "<target_expression> = <value_expression>")]
    public class ScriptAssignExpression : ScriptExpression
    {
        public ScriptExpression Target { get; set; }

        public ScriptExpression Value { get; set; }

        public override object Evaluate(TemplateContext context)
        {
            var valueObject = context.Evaluate(Value);
            context.SetValue(Target, valueObject);
            return valueObject;
        }

        protected override void WriteImpl(RenderContext context)
        {
            Target?.Write(context);
            context.Write("=").WithSpace();
            Value?.Write(context);
        }

        public override string ToString()
        {
            return $"{Target} = {Value}";
        }
    }
}