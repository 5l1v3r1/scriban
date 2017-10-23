﻿// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license. 
// See license.txt file in the project root for full license information.
using System;
using System.Reflection;
using Scriban.Helpers;
using Scriban.Parsing;
using Scriban.Syntax;

namespace Scriban.Runtime
{
    /// <summary>
    /// Extensions attached to an <see cref="IScriptObject"/>.
    /// </summary>
    public static class ScriptObjectExtensions
    {
        /// <summary>
        /// Allows to filter a member.
        /// </summary>
        /// <param name="member">The member.</param>
        /// <returns></returns>
        public delegate bool FilterMemberDelegate(string member);

        /// <summary>
        /// Asserts that the specified script object is not readonly or throws a <see cref="ScriptRuntimeException"/>
        /// </summary>
        /// <param name="scriptObject">The script object.</param>
        /// <exception cref="ScriptRuntimeException">If the object is not readonly</exception>
        public static void AssertNotReadOnly(this IScriptObject scriptObject)
        {
            if (scriptObject.IsReadOnly)
            {
                throw new InvalidOperationException("The object is readonly");
            }
        }

        /// <summary>
        /// Imports the specified object intto this <see cref="ScriptObject"/> context. See remarks.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <param name="filter">Optional member filterer</param>
        /// <param name="renamer">Optional renamer</param>
        /// <remarks>
        /// <ul>
        /// <li>If <paramref name="obj"/> is a <see cref="System.Type"/>, this method will import only the static field/properties of the specified object.</li>
        /// <li>If <paramref name="obj"/> is a <see cref="ScriptObject"/>, this method will import the members of the specified object into the new object.</li>
        /// <li>If <paramref name="obj"/> is a plain object, this method will import the public fields/properties of the specified object into the <see cref="ScriptObject"/>.</li>
        /// </ul>
        /// </remarks>
        public static void Import(this IScriptObject script, object obj, FilterMemberDelegate filter = null, MemberRenamerDelegate renamer = null)
        {
            if (obj is IScriptObject)
            {
                script.Import((IScriptObject)obj);
                return;
            }

            script.Import(obj, ScriptMemberImportFlags.All, filter, renamer);
        }

        public static bool TryGetValue(this IScriptObject @this, string key, out object value)
        {
            return @this.TryGetValue(null, new SourceSpan(), key, out value);
        }

        /// <summary>
        /// Tries to set the value and readonly state of the specified member.
        /// </summary>
        /// <param name="member">The member.</param>
        /// <param name="value">The value.</param>
        /// <param name="readOnly">if set to <c>true</c> the value will be read only.</param>
        /// <returns><c>true</c> if the value could be set; <c>false</c> if a value already exist an is readonly</returns>
        public static bool TrySetValue(this IScriptObject @this, string member, object value, bool readOnly)
        {
            if (!@this.CanWrite(member))
            {
                return false;
            }
            @this.SetValue(null, new SourceSpan(), member, value, readOnly);
            return true;
        }

        /// <summary>
        /// Imports the specified <see cref="ScriptObject"/> into this instance by copying the member values into this object.
        /// </summary>
        /// <param name="other">The other <see cref="ScriptObject"/>.</param>
        public static void Import(this IScriptObject @this, IScriptObject other)
        {
            if (other == null)
            {
                return;
            }

            var thisScript = @this.GetScriptObject();
            AssertNotReadOnly(thisScript);
            var otherScript = other.GetScriptObject();

            foreach (var keyValue in otherScript.Store)
            {
                var member = keyValue.Key;
                if (!thisScript.CanWrite(member))
                {
                    continue;
                }
                thisScript.Store[keyValue.Key] = keyValue.Value;
            }
        }

        /// <summary>
        /// Gets the script object attached to the specified instance.
        /// </summary>
        /// <param name="this">The script object proxy.</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException">Expecting ScriptObject or ScriptArray instance</exception>
        public static ScriptObject GetScriptObject(this IScriptObject @this)
        {
            var script = @this as ScriptObject;
            if (script == null)
            {
                var scriptArray = @this as ScriptArray;
                if (scriptArray == null)
                {
                    throw new ArgumentException("Expecting ScriptObject or ScriptArray instance", nameof(@this));
                }
                script = scriptArray.ScriptObject;
            }
            return script;
        }


        /// <summary>
        /// Imports a specific member from the specified object.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <param name="memberName">Name of the member.</param>
        /// <param name="exportName">Name of the member name replacement. If null, use the default renamer will be used.</param>
        public static void ImportMember(this IScriptObject script, object obj, string memberName, string exportName = null)
        {
            script.Import(obj, ScriptMemberImportFlags.All | ScriptMemberImportFlags.MethodInstance, member => member == memberName, exportName != null ? name => exportName: (MemberRenamerDelegate)null);
        }


        /// <summary>
        /// Imports the specified object.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <param name="flags">The import flags.</param>
        /// <param name="filter">A filter applied on each member</param>
        /// <param name="renamer">The member renamer.</param>
        /// <exception cref="System.ArgumentOutOfRangeException"></exception>
        public static void Import(this IScriptObject script, object obj, ScriptMemberImportFlags flags, FilterMemberDelegate filter = null, MemberRenamerDelegate renamer = null)
        {
            if (obj == null)
            {
                return;
            }
            if (!ScriptObject.IsImportable(obj))
            {
                throw new ArgumentOutOfRangeException(nameof(obj), $"Unsupported object type [{obj.GetType()}]. Expecting plain class or struct");
            }

            var typeInfo = (obj as Type ?? obj.GetType()).GetTypeInfo();
            bool useStatic = false;
            bool useInstance = false;
            bool useMethodInstance = false;
            if (obj is Type)
            {
                useStatic = true;
                obj = null;
            }
            else
            {
                useInstance = true;
                useMethodInstance = (flags & ScriptMemberImportFlags.MethodInstance) != 0;
            }

            renamer = renamer ?? StandardMemberRenamer.Default;

            if ((flags & ScriptMemberImportFlags.Field) != 0)
            {
                foreach (var field in typeInfo.GetDeclaredFields())
                {
                    if (!field.IsPublic)
                    {
                        continue;
                    }
                    if (filter != null && !filter(field.Name))
                    {
                        continue;
                    }

                    var keep = field.GetCustomAttribute<ScriptMemberIgnoreAttribute>() == null;
                    if (keep && ((field.IsStatic && useStatic) || useInstance))
                    {
                        var newFieldName = renamer(field.Name);
                        if (String.IsNullOrEmpty(newFieldName))
                        {
                            newFieldName = field.Name;
                        }

                        // If field is init only or literal, it cannot be set back so we mark it as read-only
                        script.SetValue(null, new SourceSpan(), newFieldName, field.GetValue(obj), field.IsInitOnly || field.IsLiteral);
                    }
                }
            }

            if ((flags & ScriptMemberImportFlags.Property) != 0)
            {
                foreach (var property in typeInfo.GetDeclaredProperties())
                {
                    if (!property.CanRead || !property.GetGetMethod().IsPublic)
                    {
                        continue;
                    }

                    if (filter != null && !filter(property.Name))
                    {
                        continue;
                    }

                    var keep = property.GetCustomAttribute<ScriptMemberIgnoreAttribute>() == null;
                    if (keep && (((property.GetGetMethod().IsStatic && useStatic) || useInstance)))
                    {
                        var newPropertyName = renamer(property.Name);
                        if (String.IsNullOrEmpty(newPropertyName))
                        {
                            newPropertyName = property.Name;
                        }

                        script.SetValue(null, new SourceSpan(), newPropertyName, property.GetValue(obj), property.GetSetMethod() == null || !property.GetSetMethod().IsPublic);
                    }
                }
            }

            if ((flags & ScriptMemberImportFlags.Method) != 0 && (useStatic || useMethodInstance))
            {
                foreach (var method in typeInfo.GetDeclaredMethods())
                {
                    if (filter != null && !filter(method.Name))
                    {
                        continue;
                    }

                    var keep = method.GetCustomAttribute<ScriptMemberIgnoreAttribute>() == null;
                    if (keep && method.IsPublic && ((useMethodInstance && !method.IsStatic) || (useStatic && method.IsStatic)) && !method.IsSpecialName)
                    {
                        var newMethodName = renamer(method.Name);
                        if (String.IsNullOrEmpty(newMethodName))
                        {
                            newMethodName = method.Name;
                        }

                        script.SetValue(null, new SourceSpan(), newMethodName, new ObjectFunctionWrapper(obj, method), true);
                    }
                }
            }
        }

        /// <summary>
        /// Imports the delegate to the specified member.
        /// </summary>
        /// <param name="member">The member.</param>
        /// <param name="function">The function delegate.</param>
        /// <exception cref="System.ArgumentNullException">if member or function are null</exception>
        public static void Import(this IScriptObject script, string member, Delegate function)
        {
            if (member == null) throw new ArgumentNullException(nameof(member));
            if (function == null) throw new ArgumentNullException(nameof(function));

            script.SetValue(null, new SourceSpan(), member, new ObjectFunctionWrapper(function.Target, function.GetMethodInfo()), true);
        }

        private class ObjectFunctionWrapper : IScriptCustomFunction
        {
            private readonly object target;
            private readonly MethodInfo method;
            private readonly ParameterInfo[] parametersInfo;
            private readonly bool hasObjectParams;
            private readonly int lastParamsIndex;
            private readonly bool hasTemplateContext;
            private readonly bool hasSpan;

            public ObjectFunctionWrapper(object target, MethodInfo method)
            {
                this.target = target;
                this.method = method;
                parametersInfo = method.GetParameters();
                lastParamsIndex = parametersInfo.Length - 1;
                if (parametersInfo.Length > 0)
                {
                    // Check if we have TemplateContext+SourceSpan as first parameters
                    if (typeof(TemplateContext).GetTypeInfo().IsAssignableFrom(parametersInfo[0].ParameterType.GetTypeInfo()))
                    {
                        hasTemplateContext = true;
                        if (parametersInfo.Length > 1)
                        {
                            hasSpan = typeof(SourceSpan).GetTypeInfo().IsAssignableFrom(parametersInfo[1].ParameterType.GetTypeInfo());
                        }
                    }

                    var lastParam = parametersInfo[lastParamsIndex];

                    if (lastParam.ParameterType == typeof(object[]))
                    {
                        foreach (var param in lastParam.GetCustomAttributes(typeof(ParamArrayAttribute), false))
                        {
                            hasObjectParams = true;
                            break;
                        }
                    }
                }
            }

            public object Invoke(TemplateContext context, ScriptNode callerContext, ScriptArray parameters, ScriptBlockStatement blockStatement)
            {
                var expectedNumberOfParameters = parametersInfo.Length;
                if (hasTemplateContext)
                {
                    expectedNumberOfParameters--;
                    if (hasSpan)
                    {
                        expectedNumberOfParameters--;
                    }
                }

                // Check parameters
                if ((hasObjectParams && parameters.Count < expectedNumberOfParameters - 1) || (!hasObjectParams && parameters.Count != expectedNumberOfParameters))
                {
                    throw new ScriptRuntimeException(callerContext.Span, $"Invalid number of arguments passed [{parameters.Count}] while expecting [{expectedNumberOfParameters}] for [{callerContext}]");
                }

                // Convert arguments
                var arguments = new object[parametersInfo.Length];
                object[] paramArguments = null;
                if (hasObjectParams)
                {
                    paramArguments = new object[parameters.Count - lastParamsIndex];
                    arguments[lastParamsIndex] = paramArguments;
                }

                // Copy TemplateContext/SourceSpan parameters
                int argIndex = 0;
                if (hasTemplateContext)
                {
                    arguments[0] = context;
                    argIndex++;
                    if (hasSpan)
                    {
                        arguments[1] = callerContext.Span;
                        argIndex++;
                    }
                }

                for (int i = 0; i < parameters.Count; i++, argIndex++)
                {
                    var destType = hasObjectParams && i >= lastParamsIndex ? typeof(object) : parametersInfo[argIndex].ParameterType;
                    try
                    {
                        var argValue = context.ToObject(callerContext.Span, parameters[i], destType);
                        if (hasObjectParams && i >= lastParamsIndex)
                        {
                            paramArguments[argIndex - lastParamsIndex] = argValue;
                        }
                        else
                        {
                            arguments[argIndex] = argValue;
                        }
                    }
                    catch (Exception exception)
                    {
                        throw new ScriptRuntimeException(callerContext.Span, $"Unable to convert parameter #{i} of type [{parameters[i]?.GetType()}] to type [{destType}]", exception);
                    }
                }

                // Call method
                try
                {
                    var result = method.Invoke(target, arguments);
                    return result;
                }
                catch (Exception exception)
                {
                    throw new ScriptRuntimeException(callerContext.Span, $"Unexpected exception when calling {callerContext}", exception);
                }
            }
        }
    }
}