﻿/*
    MIT License

    Copyright(c) 2014-2018 Infragistics, Inc.
    Copyright 2018 Google LLC

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.
*/

using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace BreakingChangesDetector.MetadataItems
{
    // TODO: Remove 'Generic' to be consistent with Roslyn?

    /// <summary>
    /// Represents the metadata for an generic type parameter defined on an externally visible generic type or method.
    /// </summary>
    public sealed class GenericTypeParameterData : TypeData
    {
        private const string InVarianceModifier = "in ";
        private const string OutVarianceModifier = "out ";

        internal new static readonly GenericTypeParameterCollection EmptyList = new GenericTypeParameterCollection();

        private bool _isInIsEquivalentToNewMember;

        internal GenericTypeParameterData(string name, Accessibility accessibility, MemberFlags memberFlags, TypeKind typeKind, AssemblyData assembly, System.Reflection.GenericParameterAttributes genericParameterAttributes, int genericParameterPosition)
            : base(name, accessibility, memberFlags, typeKind)
        {
            AssemblyData = assembly;
            GenericParameterAttributes = genericParameterAttributes;
            GenericParameterPosition = genericParameterPosition;
        }

        internal GenericTypeParameterData(ITypeParameterSymbol typeParameterSymbol, AssemblyData assembly)
            : base(typeParameterSymbol, declaringType: null)
        {
            AssemblyData = assembly;

            // TODO_Refactor: should we expose this info as 4 properties now, like Roslyn?
            switch (typeParameterSymbol.Variance)
            {
                case VarianceKind.In:
                    GenericParameterAttributes |= GenericParameterAttributes.Contravariant;
                    break;
                case VarianceKind.Out:
                    GenericParameterAttributes |= GenericParameterAttributes.Covariant;
                    break;
            }

            if (typeParameterSymbol.HasReferenceTypeConstraint)
            {
                GenericParameterAttributes |= GenericParameterAttributes.ReferenceTypeConstraint;
            }
            if (typeParameterSymbol.HasValueTypeConstraint)
            {
                GenericParameterAttributes |= GenericParameterAttributes.NotNullableValueTypeConstraint;
            }
            if (typeParameterSymbol.HasConstructorConstraint)
            {
                GenericParameterAttributes |= GenericParameterAttributes.DefaultConstructorConstraint;
            }

            GenericParameterPosition = typeParameterSymbol.Ordinal;
        }

        /// <inheritdoc/>
        public override void Accept(MetadataItemVisitor visitor) => visitor.VisitGenericTypeParameterData(this);

        /// <inheritdoc/>
        public override AssemblyData AssemblyData { get; }

        internal override bool DoesMatch(MetadataItemBase other)
        {
            if (base.DoesMatch(other) == false)
            {
                return false;
            }

            var otherTyped = other as GenericTypeParameterData;
            if (otherTyped == null)
            {
                return false;
            }

            if (Constraints.Count != otherTyped.Constraints.Count)
            {
                return false;
            }

            for (int i = 0; i < Constraints.Count; i++)
            {
                if (Constraints[i].DisplayName != otherTyped.Constraints[i].DisplayName)
                {
                    return false;
                }
            }

            if (GenericDeclaringMember.DisplayName != otherTyped.GenericDeclaringMember.DisplayName)
            {
                return false;
            }

            if (GenericParameterAttributes != otherTyped.GenericParameterAttributes)
            {
                return false;
            }

            if (GenericParameterPosition != otherTyped.GenericParameterPosition)
            {
                return false;
            }

            return true;
        }

        /// <inheritdoc/>
        internal override IEnumerable<TypeData> GetDirectImplicitConversions(bool onlyReferenceAndIdentityConversions)
        {
            if (onlyReferenceAndIdentityConversions && IsValueType)
            {
                yield break;
            }

            foreach (var constraint in Constraints)
            {
                yield return constraint;
            }

            // Any generic type parameter can convert to object
            var mscorlibData = AssemblyData.GetReferencedAssembly(Utilities.CommonObjectRuntimeAssemblyName);
            if (mscorlibData != null)
            {
                yield return mscorlibData.GetTypeDefinitionData(Utilities.ObjectTypeName);
            }
        }

        /// <inheritdoc/>
        public override string GetDisplayName(bool fullyQualify = true, bool includeGenericInfo = true) => Name;

        /// <inheritdoc/>
        internal override TypeData GetEquivalentNewType(AssemblyFamily newAssemblyFamily)
        {
            if (GenericDeclaringMember is TypeDefinitionData declaringGenericType)
            {
                Debug.Assert(this == declaringGenericType.GenericParameters[GenericParameterPosition], "This type should be the generic parameter at its position in the declaring type.");

                var newGenericType = (TypeDefinitionData)declaringGenericType.GetEquivalentNewType(newAssemblyFamily);
                if (newGenericType == null || newGenericType.GenericParameters.Count <= GenericParameterPosition)
                {
                    return null;
                }

                return newGenericType.GenericParameters[GenericParameterPosition];
            }

            if (GenericDeclaringMember is MethodData declaringGenericMethod)
            {
                Debug.Assert(this == declaringGenericMethod.GenericParameters[GenericParameterPosition], "This type should be the generic parameter at its position in the declaring method.");

                var newDeclaringType = (DeclaringTypeData)declaringGenericMethod.ContainingType.GetEquivalentNewType(newAssemblyFamily);
                if (newDeclaringType == null)
                {
                    return null;
                }

                var matchingMethods = newDeclaringType.GetMembers(declaringGenericMethod.Name).OfType<MethodData>().Where(m => declaringGenericMethod.IsEquivalentToNewMember(m, newAssemblyFamily)).ToList();
                if (matchingMethods.Count == 0)
                {
                    return null;
                }

                Debug.Assert(matchingMethods.Count == 1, "There should only be one matching method.");
                var newGenericMethod = matchingMethods[0];
                if (newGenericMethod.GenericParameters.Count <= GenericParameterPosition)
                {
                    return null;
                }

                return newGenericMethod.GenericParameters[GenericParameterPosition];
            }

            Debug.Fail("Unknown owner of the generic parameter");
            return null;
        }

        internal override bool IsEquivalentToNewMember(MemberDataBase newMember, AssemblyFamily newAssemblyFamily)
        {
            if (base.IsEquivalentToNewMember(newMember, newAssemblyFamily) == false)
            {
                return false;
            }

            var newGenericParameter = (GenericTypeParameterData)newMember;
            if (GenericParameterPosition != newGenericParameter.GenericParameterPosition)
            {
                return false;
            }

            if (GenericDeclaringMember.MetadataItemKind == MetadataItemKinds.Method &&
                newGenericParameter.GenericDeclaringMember.MetadataItemKind == MetadataItemKinds.Method)
            {
                // We will get in here recursively for generic methods that take one of their own generic parameters as a parameter
                // because the method will check to see whether its parameters are equivalent. All other things being equal, we don't
                // need to recheck that the methods are equal here (and if we do, we will end up with a SOE). If the methods are 
                // otherwise equal, so are these parameters because they came from the same method and had the same position. 
                // If the methods are not, neither are the parameters.
                if (_isInIsEquivalentToNewMember)
                {
                    return true;
                }

                try
                {
                    _isInIsEquivalentToNewMember = true;
                    return GenericDeclaringMember.IsEquivalentToNewMember(newGenericParameter.GenericDeclaringMember, newAssemblyFamily);
                }
                finally
                {
                    _isInIsEquivalentToNewMember = false;
                }
            }

            return GenericDeclaringMember.IsEquivalentToNewMember(newGenericParameter.GenericDeclaringMember, newAssemblyFamily);
        }

        /// <inheritdoc/>
        public override MetadataItemKinds MetadataItemKind => MetadataItemKinds.GenericTypeParameter;

        /// <inheritdoc/>
        internal override MemberDataBase ReplaceGenericTypeParameters(GenericTypeParameterCollection genericParameters, GenericTypeArgumentCollection genericArguments)
        {
            for (int i = 0; i < genericParameters.Count; i++)
            {
                if (this == genericParameters[i])
                {
                    // TODO_Refactor: Re-add something like this?
                    //Debug.Assert(this.GenericParameterPosition == i, "We are expecting the generic argument to be in its proper position.");
                    return genericArguments[i];
                }
            }

            return this;
        }

        /// <summary>
        /// Populates the type with additional information which can't be loaded when the type is created (due to potential circularities in item dependencies).
        /// </summary>
        /// <param name="underlyingTypeSymbol">The underlying type this instance represents.</param>
        internal void FinalizeDefinition(ITypeParameterSymbol underlyingTypeSymbol)
        {
            foreach (var type in underlyingTypeSymbol.ConstraintTypes)
            {
                Constraints.Add(Context.GetTypeData(type));
            }
        }

        /// <summary>
        /// Gets the display text for the type parameter when it is being displayed in a parameter list.
        /// </summary> 
        internal string GetParameterListDisplayText()
        {
            switch (GenericParameterAttributes & GenericParameterAttributes.VarianceMask)
            {
                case GenericParameterAttributes.None:
                    return Name;

                case GenericParameterAttributes.Covariant:
                    return OutVarianceModifier + Name;

                case GenericParameterAttributes.Contravariant:
                    return InVarianceModifier + Name;

                default:
                    Debug.Fail("Unknown GenericParameterAttributes: " + GenericParameterAttributes);
                    return Name;
            }
        }

        /// <summary>
        /// Determines whether the variance of the type parameter allows the source type in the parameter's position to vary to the target type when the declaring
        /// generic of one construction is trying to be converted to a another construction.
        /// </summary>
        /// <param name="targetArgument">The type in this parameter's position in the target constructed generic type.</param>
        /// <param name="sourceArgument">The type in this parameter's position in the source constructed generic type.</param>
        /// <param name="context">Information about the context of the IsAssignableFrom invocation.</param>
        internal bool IsGenericTypeArgumentVariant(TypeData targetArgument, TypeData sourceArgument, IsAssignableFromContext context)
        {
            var variance = GenericParameterAttributes & GenericParameterAttributes.VarianceMask;
            switch (variance)
            {
                case GenericParameterAttributes.None:
                    return targetArgument.IsEquivalentTo(sourceArgument, context.NewAssemblyFamily, context.IsSourceTypeOld);

                case GenericParameterAttributes.Contravariant:
                    return sourceArgument.IsAssignableFrom(targetArgument, new IsAssignableFromContext(context.NewAssemblyFamily, context.IsSourceTypeOld, onlyReferenceAndIdentityConversions: true));

                case GenericParameterAttributes.Covariant:
                    return targetArgument.IsAssignableFrom(sourceArgument, new IsAssignableFromContext(context.NewAssemblyFamily, context.IsSourceTypeOld, onlyReferenceAndIdentityConversions: true));

                default:
                    Debug.Fail("Unknown variance: " + variance);
                    return false;
            }
        }

        /// <summary>
        /// Gets the set of constraints imposed on the generic type parameter.
        /// </summary>
        public List<TypeData> Constraints { get; } = new List<TypeData>();

        /// <summary>
        /// Gets the method or delegate which defines the generic type parameter.
        /// </summary>
        public MemberDataBase GenericDeclaringMember { get; internal set; } // TODO_Serialize: Test and round-trip

        /// <summary>
        /// Gets the attributes (variance and additional constraints) of the generic type parameter.
        /// </summary>
        public System.Reflection.GenericParameterAttributes GenericParameterAttributes { get; }

        /// <summary>
        /// Gets the 0-based position of the parameter in the declaring type's generic type parameter list.
        /// </summary>
        public int GenericParameterPosition { get; }
    }
}
