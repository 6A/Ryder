﻿using System;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;

namespace Ryder
{
    /// <summary>
    ///   Defines a class that can redirect calls.
    /// </summary>
    public abstract class Redirection : IDisposable
    {
        protected bool isRedirecting;

        /// <summary>
        ///   Gets or sets whether or not the calls shall be redirected.
        /// </summary>
        public bool IsRedirecting
        {
            get => isRedirecting;
            set
            {
                if (value == isRedirecting)
                    return;

                if (value)
                    Start();
                else
                    Stop();
            }
        }

        internal Redirection() { }

        /// <summary>
        ///   Starts redirecting calls.
        /// </summary>
        public abstract void Start();

        /// <summary>
        ///   Stops redirecting calls.
        /// </summary>
        public abstract void Stop();

        /// <summary>
        ///   Disposes of the <see cref="Redirection"/>,
        ///   disabling it and removing static references made to
        ///   the needed objects.
        /// </summary>
        public abstract void Dispose();

        #region Static

        private const string AbstractError = "Expected non-abstract method.";
        private const string SignatureError = "Expected same signature.";

        private static void CheckParameters(ParameterInfo a, ParameterInfo b, string paramName)
        {
            Debug.Assert(a != null);
            Debug.Assert(b != null);

            if (a.ParameterType != b.ParameterType)
                throw new ArgumentException($"Expected parameters '{a}' and '{b}' to have the same type.", paramName);
            if (a.IsOut != b.IsOut || a.IsIn != b.IsIn)
                throw new ArgumentException($"Expected parameters '{a}' and '{b}' to have the same signature.", paramName);
        }

        /// <summary>
        ///   Redirects calls to the <paramref name="original"/> method
        ///   to the <paramref name="replacement"/> method.
        /// </summary>
        /// <param name="original">The <see cref="MethodBase"/> of the method whose calls shall be redirected.</param>
        /// <param name="replacement">The <see cref="MethodBase"/> of the method providing the redirection.</param>
        public static MethodRedirection Redirect(MethodBase original, MethodBase replacement)
        {
            if (original == null)
                throw new ArgumentNullException(nameof(original));
            if (replacement == null)
                throw new ArgumentNullException(nameof(replacement));

            // Check if abstract
            if (original.IsAbstract)
                throw new ArgumentException(AbstractError, nameof(original));
            if (replacement.IsAbstract)
                throw new ArgumentException(AbstractError, nameof(replacement));

            // Check if different kind
            if (original.IsConstructor != replacement.IsConstructor)
                throw new ArgumentException("Expected both methods to be of the same kind (ctor or method).", nameof(replacement));
            
            // Get return type
            Type originalReturnType = (original as MethodInfo)?.ReturnType ?? (original as ConstructorInfo)?.DeclaringType;

            if (originalReturnType == null)
                throw new ArgumentException("Invalid method.", nameof(original));

            Type replacementReturnType = (replacement as MethodInfo)?.ReturnType ?? (replacement as ConstructorInfo)?.DeclaringType;

            if (replacementReturnType == null)
                throw new ArgumentException("Invalid method.", nameof(replacement));

            // Check return type
            if (originalReturnType != replacementReturnType)
                throw new ArgumentException("Expected both methods to have the same return type.", nameof(replacement));

            // Check signature
            ParameterInfo[] originalParams = original.GetParameters();
            ParameterInfo[] replacementParams = replacement.GetParameters();

            int length = originalParams.Length;
            int diff = 0;

            if (!original.IsStatic)
            {
                if (replacement.IsStatic)
                {
                    // Should have:
                    // instance i.original(a, b) | static replacement(i, a, b)

                    if (replacementParams.Length == 0 || replacementParams[0].ParameterType != original.DeclaringType)
                        throw new ArgumentException($"Expected first parameter of type '{original.DeclaringType}'.", nameof(replacement));
                    if (replacementParams.Length != originalParams.Length + 1)
                        throw new ArgumentException(SignatureError, nameof(replacement));

                    diff = -1;
                    // No need to set length, it's already good
                }
                else
                {
                    // Should have:
                    // instance i.original(a, b) | instance i.replacement(a, b)
                    
                    if (replacementParams.Length != originalParams.Length)
                        throw new ArgumentException(SignatureError, nameof(replacement));
                }
            }
            else if (!replacement.IsStatic)
            {
                // Should have:
                // static original(i, a, b) | instance i.replacement(a, b)

                if (originalParams.Length == 0 || originalParams[0].ParameterType != replacement.DeclaringType)
                    throw new ArgumentException($"Expected first parameter of type '{replacement.DeclaringType}'.", nameof(original));
                if (replacementParams.Length != originalParams.Length - 1)
                    throw new ArgumentException(SignatureError, nameof(replacement));

                diff = 1;
                length--;
            }
            else
            {
                // Should have:
                // static original(a, b) | static replacement(a, b)

                if (originalParams.Length != replacementParams.Length)
                    throw new ArgumentException(SignatureError, nameof(replacement));
            }

            // At this point all parameters will have the same index with "+ diff",
            // and the parameters not checked in this loop have already been checked. We good.
            for (int i = diff == -1 ? 1 : 0; i < length; i++)
            {
                CheckParameters(originalParams[i + diff], replacementParams[i], nameof(replacement));
            }

            return new MethodRedirection(original, replacement, true);
        }

        /// <summary>
        ///   Redirects calls to the <paramref name="original"/> delegate
        ///   to the <paramref name="replacement"/> delegate.
        /// </summary>
        /// <param name="original">The <see cref="Delegate"/> whose calls shall be redirected.</param>
        /// <param name="replacement">The <see cref="Delegate"/> providing the redirection.</param>
        public static MethodRedirection Redirect(Delegate original, Delegate replacement)
        {
            if (original == null)
                throw new ArgumentNullException(nameof(original));
            if (replacement == null)
                throw new ArgumentNullException(nameof(replacement));

            return Redirect(original.GetMethodInfo(), replacement.GetMethodInfo());
        }

        /// <summary>
        ///   Redirects calls to the <paramref name="original"/> method
        ///   to the <paramref name="replacement"/> method.
        /// </summary>
        /// <param name="original">The <see cref="Delegate"/> whose calls shall be redirected.</param>
        /// <param name="replacement">The <see cref="Delegate"/> providing the redirection.</param>
        public static MethodRedirection Redirect(Expression<Action> original, Expression<Action> replacement)
        {
            if (original == null)
                throw new ArgumentNullException(nameof(original));
            if (replacement == null)
                throw new ArgumentNullException(nameof(replacement));

            return Redirect(
                (original.Body as MethodCallExpression)?.Method ?? throw new ArgumentException("Invalid expression.", nameof(original)),
                (replacement.Body as MethodCallExpression)?.Method ?? throw new ArgumentException("Invalid expression.", nameof(replacement))
            );
        }


        /// <summary>
        ///   Redirects accesses to the <paramref name="original"/> property
        ///   to the <paramref name="replacement"/> property.
        /// </summary>
        /// <param name="original">The <see cref="PropertyInfo"/> of the property whose accesses shall be redirected.</param>
        /// <param name="replacement">The <see cref="PropertyInfo"/> of the property providing the redirection.</param>
        public static PropertyRedirection Redirect(PropertyInfo original, PropertyInfo replacement)
        {
            if (original == null)
                throw new ArgumentNullException(nameof(original));
            if (replacement == null)
                throw new ArgumentNullException(nameof(replacement));

            // Check original
            MethodInfo anyOriginalMethod = original.GetMethod ?? original.SetMethod;

            if (anyOriginalMethod == null)
                throw new ArgumentException("The property must define a getter and/or a setter.", nameof(original));
            if (anyOriginalMethod.IsAbstract)
                throw new ArgumentException(AbstractError, nameof(original));

            // Check replacement
            MethodInfo anyReplacementMethod = replacement.GetMethod ?? replacement.SetMethod;

            if (anyReplacementMethod == null)
                throw new ArgumentException("The property must define a getter and/or a setter.", nameof(replacement));
            if (anyReplacementMethod.IsAbstract)
                throw new ArgumentException(AbstractError, nameof(replacement));

            // Check match: static
            if (!anyOriginalMethod.IsStatic)
            {
                if (anyReplacementMethod.IsStatic)
                    throw new ArgumentException(SignatureError, nameof(replacement));

                // Check match: instance of same type
                //if (original.DeclaringType != replacement.DeclaringType)
                //    throw new ArgumentException(SignatureError, nameof(replacement));
            }

            // Check match: event type
            if (original.PropertyType != replacement.PropertyType)
                throw new ArgumentException("Expected same property type.", nameof(replacement));

            // Check match: same declarations
            if ((original.GetMethod == null) != (replacement.GetMethod == null) ||
                (original.SetMethod == null) != (replacement.SetMethod == null))
                throw new ArgumentException(SignatureError, nameof(replacement));

            return new PropertyRedirection(original, replacement, true);
        }


        /// <summary>
        ///   Redirects accesses to the <paramref name="original"/> event
        ///   to the <paramref name="replacement"/> event.
        /// </summary>
        /// <param name="original">The <see cref="EventInfo"/> of the event whose accesses shall be redirected.</param>
        /// <param name="replacement">The <see cref="EventInfo"/> of the event providing the redirection.</param>
        public static EventRedirection Redirect(EventInfo original, EventInfo replacement)
        {
            if (original == null)
                throw new ArgumentNullException(nameof(original));
            if (replacement == null)
                throw new ArgumentNullException(nameof(replacement));

            // Check original
            MethodInfo anyOriginalMethod = original.AddMethod ?? original.RemoveMethod ?? original.RaiseMethod;

            if (anyOriginalMethod == null)
                throw new ArgumentException("The event must define an add and/or remove and/or raise method.", nameof(original));
            if (anyOriginalMethod.IsAbstract)
                throw new ArgumentException(AbstractError, nameof(original));

            // Check replacement
            MethodInfo anyReplacementMethod = replacement.AddMethod ?? replacement.RemoveMethod ?? replacement.RaiseMethod;

            if (anyReplacementMethod == null)
                throw new ArgumentException("The event must define an add and/or remove and/or raise method.", nameof(replacement));
            if (anyReplacementMethod.IsAbstract)
                throw new ArgumentException(AbstractError, nameof(replacement));

            // Check match: static
            if (!anyOriginalMethod.IsStatic)
            {
                if (anyReplacementMethod.IsStatic)
                    throw new ArgumentException(SignatureError, nameof(replacement));

                // Check match: instance of same type
                if (original.DeclaringType != replacement.DeclaringType)
                    throw new ArgumentException(SignatureError, nameof(replacement));
            }

            // Check match: event type
            if (original.EventHandlerType != replacement.EventHandlerType)
                throw new ArgumentException("Expected same event handler type.", nameof(replacement));

            // Check match: same declarations
            if ((original.AddMethod == null) != (replacement.AddMethod == null) ||
                (original.RemoveMethod == null) != (replacement.RemoveMethod == null) ||
                (original.RaiseMethod == null) != (replacement.RaiseMethod == null))
                throw new ArgumentException(SignatureError, nameof(replacement));

            return new EventRedirection(original, replacement, true);
        }


        /// <summary>
        ///   Redirects accesses to the <paramref name="original"/> member
        ///   to the <paramref name="replacement"/> member.
        /// </summary>
        /// <param name="original">
        ///   A <see cref="LambdaExpression"/> describing the member whose accesses shall be redirected.
        /// </param>
        /// <param name="replacement">
        ///   A <see cref="LambdaExpression"/> describing the member providing the redirection.
        /// </param>
        public static Redirection Redirect<T>(Expression<Func<T>> original, Expression<Func<T>> replacement)
        {
            if (original == null)
                throw new ArgumentNullException(nameof(original));
            if (replacement == null)
                throw new ArgumentNullException(nameof(replacement));

            // Make sure both expressions have the same type
            if (original.NodeType != replacement.NodeType)
                throw new ArgumentException($"Expected the '{original.NodeType}' NodeType.", nameof(replacement));

            // Maybe it's a method call?
            if (original.Body is MethodCallExpression originalCall &&
                replacement.Body is MethodCallExpression replacementCall)
            {
                return Redirect(originalCall.Method, replacementCall.Method);
            }

            // Then it has to be a member access.
            if (original.Body is MemberExpression originalMember &&
                     replacement.Body is MemberExpression replacementMember)
            {
                // Probably a property?
                if (originalMember.Member is PropertyInfo originalProp &&
                    replacementMember.Member is PropertyInfo replacementProp)
                {
                    return Redirect(originalProp, replacementProp);
                }

                // Then it has to be an event.
                if (originalMember.Member is EventInfo originalEvent &&
                    replacementMember.Member is EventInfo replacementEvent)
                {
                    return Redirect(originalEvent, replacementEvent);
                }

                // Not a property nor an event: it's an error
                throw new InvalidOperationException("The given member must be a property or an event.");
            }

            // Not a member access nor a call: it's an error.
            throw new InvalidOperationException($"The given expressions must be of type '{ExpressionType.MemberAccess}' or '{ExpressionType.Call}'.");
        }

        /// <summary>
        ///   Redirects accesses to the <paramref name="original"/> member
        ///   to the <paramref name="replacement"/> member.
        /// </summary>
        /// <param name="original">
        ///   A <see cref="LambdaExpression"/> describing the member whose accesses shall be redirected.
        /// </param>
        /// <param name="replacement">
        ///   A <see cref="LambdaExpression"/> describing the member providing the redirection.
        /// </param>
        public static TRedirection Redirect<T, TRedirection>(Expression<Func<T>> original, Expression<Func<T>> replacement)
            where TRedirection : Redirection => (TRedirection)Redirect(original, replacement);
        #endregion
    }
}