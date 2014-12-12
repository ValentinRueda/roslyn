// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Composition;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CodeGeneration;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.LanguageServices;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.CodeGeneration
{
    [ExportLanguageService(typeof(SyntaxGenerator), LanguageNames.CSharp), Shared]
    internal class CSharpSyntaxGenerator : SyntaxGenerator
    {
        public override SyntaxNode CompilationUnit(IEnumerable<SyntaxNode> declarations)
        {
            return SyntaxFactory.CompilationUnit()
                .WithUsings(AsUsingDirectives(declarations))
                .WithMembers(AsNamespaceMembers(declarations));
        }

        private SyntaxList<UsingDirectiveSyntax> AsUsingDirectives(IEnumerable<SyntaxNode> declarations)
        {
            return (declarations != null)
                ? SyntaxFactory.List(declarations.Select(AsUsingDirective).OfType<UsingDirectiveSyntax>())
                : default(SyntaxList<UsingDirectiveSyntax>);
        }

        private SyntaxNode AsUsingDirective(SyntaxNode node)
        {
            var name = node as NameSyntax;
            if (name != null)
            {
                return this.NamespaceImportDeclaration(name);
            }

            return node as UsingDirectiveSyntax;
        }

        private SyntaxList<MemberDeclarationSyntax> AsNamespaceMembers(IEnumerable<SyntaxNode> declarations)
        {
            return (declarations != null)
                ? SyntaxFactory.List(declarations.OfType<MemberDeclarationSyntax>())
                : default(SyntaxList<MemberDeclarationSyntax>);
        }

        public override SyntaxNode NamespaceImportDeclaration(SyntaxNode name)
        {
            return SyntaxFactory.UsingDirective((NameSyntax)name);
        }

        public override SyntaxNode NamespaceDeclaration(SyntaxNode name, IEnumerable<SyntaxNode> declarations)
        {
            return SyntaxFactory.NamespaceDeclaration(
                (NameSyntax)name,
                default(SyntaxList<ExternAliasDirectiveSyntax>),
                AsUsingDirectives(declarations),
                AsNamespaceMembers(declarations));
        }

        public override SyntaxNode FieldDeclaration(
            string name,
            SyntaxNode type,
            Accessibility accessibility,
            DeclarationModifiers modifiers,
            SyntaxNode initializer)
        {
            return SyntaxFactory.FieldDeclaration(
                default(SyntaxList<AttributeListSyntax>),
                AsModifierList(accessibility, modifiers, SyntaxKind.FieldDeclaration),
                SyntaxFactory.VariableDeclaration(
                    (TypeSyntax)type,
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(
                            name.ToIdentifierToken(),
                            default(BracketedArgumentListSyntax),
                            initializer != null ? SyntaxFactory.EqualsValueClause((ExpressionSyntax)initializer) : null))));
        }

        public override SyntaxNode ParameterDeclaration(string name, SyntaxNode type, SyntaxNode initializer, RefKind refKind)
        {
            return SyntaxFactory.Parameter(
                default(SyntaxList<AttributeListSyntax>),
                GetParameterModifiers(refKind),
                (TypeSyntax)type,
                name.ToIdentifierToken(),
                initializer != null ? SyntaxFactory.EqualsValueClause((ExpressionSyntax)initializer) : null);
        }

        private SyntaxTokenList GetParameterModifiers(RefKind refKind)
        {
            switch (refKind)
            {
                case RefKind.None:
                default:
                    return default(SyntaxTokenList);
                case RefKind.Out:
                    return SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.OutKeyword));
                case RefKind.Ref:
                    return SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.RefKeyword));
            }
        }

        public override SyntaxNode MethodDeclaration(
            string name,
            IEnumerable<SyntaxNode> parameters,
            IEnumerable<string> typeParameters,
            SyntaxNode returnType,
            Accessibility accessibility,
            DeclarationModifiers modifiers,
            IEnumerable<SyntaxNode> statements)
        {
            bool hasBody = !modifiers.IsAbstract;

            return SyntaxFactory.MethodDeclaration(
                default(SyntaxList<AttributeListSyntax>),
                AsModifierList(accessibility, modifiers, SyntaxKind.MethodDeclaration),
                returnType != null ? (TypeSyntax)returnType : SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
                default(ExplicitInterfaceSpecifierSyntax),
                name.ToIdentifierToken(),
                AsTypeParameterList(typeParameters),
                AsParameterList(parameters),
                default(SyntaxList<TypeParameterConstraintClauseSyntax>),
                hasBody ? CreateBlock(statements) : null,
                !hasBody ? SyntaxFactory.Token(SyntaxKind.SemicolonToken) : default(SyntaxToken));
        }

        private ParameterListSyntax AsParameterList(IEnumerable<SyntaxNode> parameters)
        {
            return parameters != null
                ? SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(parameters.Cast<ParameterSyntax>()))
                : SyntaxFactory.ParameterList();
        }

        public override SyntaxNode ConstructorDeclaration(
            string name, 
            IEnumerable<SyntaxNode> parameters, 
            Accessibility accessibility, 
            DeclarationModifiers modifiers, 
            IEnumerable<SyntaxNode> baseConstructorArguments, 
            IEnumerable<SyntaxNode> statements)
        {
            return SyntaxFactory.ConstructorDeclaration(
                default(SyntaxList<AttributeListSyntax>),
                AsModifierList(accessibility, modifiers, SyntaxKind.ConstructorDeclaration),
                (name ?? "ctor").ToIdentifierToken(),
                AsParameterList(parameters),
                baseConstructorArguments != null ? SyntaxFactory.ConstructorInitializer(SyntaxKind.BaseConstructorInitializer, SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(baseConstructorArguments.Select(AsArgument)))) : null,
                CreateBlock(statements));
        }

        public override SyntaxNode PropertyDeclaration(
            string name,
            SyntaxNode type,
            Accessibility accessibility,
            DeclarationModifiers modifiers,
            IEnumerable<SyntaxNode> getAccessorStatements,
            IEnumerable<SyntaxNode> setAccessorStatements)
        {
            var accessors = new List<AccessorDeclarationSyntax>();
            var hasSetter = !modifiers.IsReadOnly;

            if (modifiers.IsAbstract)
            {
                getAccessorStatements = null;
                setAccessorStatements = null;
            }
            else
            {
                if (getAccessorStatements == null)
                {
                    getAccessorStatements = SpecializedCollections.EmptyEnumerable<SyntaxNode>();
                }

                if (setAccessorStatements == null && !modifiers.IsReadOnly)
                {
                    setAccessorStatements = SpecializedCollections.EmptyEnumerable<SyntaxNode>();
                }
            }

            accessors.Add(AccessorDeclaration(SyntaxKind.GetAccessorDeclaration, getAccessorStatements));

            if (hasSetter)
            {
                accessors.Add(AccessorDeclaration(SyntaxKind.SetAccessorDeclaration, setAccessorStatements));
            }

            return SyntaxFactory.PropertyDeclaration(
                default(SyntaxList<AttributeListSyntax>),
                AsModifierList(accessibility, modifiers - DeclarationModifiers.ReadOnly, SyntaxKind.PropertyDeclaration),
                (TypeSyntax)type,
                default(ExplicitInterfaceSpecifierSyntax),
                name.ToIdentifierToken(),
                SyntaxFactory.AccessorList(SyntaxFactory.List(accessors)));
        }

        public override SyntaxNode IndexerDeclaration(
            IEnumerable<SyntaxNode> parameters,
            SyntaxNode type,
            Accessibility accessibility,
            DeclarationModifiers modifiers,
            IEnumerable<SyntaxNode> getAccessorStatements,
            IEnumerable<SyntaxNode> setAccessorStatements)
        {
            var accessors = new List<AccessorDeclarationSyntax>();
            var hasSetter = !modifiers.IsReadOnly;

            if (modifiers.IsAbstract)
            {
                getAccessorStatements = null;
                setAccessorStatements = null;
            }
            else
            {
                if (getAccessorStatements == null)
                {
                    getAccessorStatements = SpecializedCollections.EmptyEnumerable<SyntaxNode>();
                }

                if (setAccessorStatements == null && !modifiers.IsReadOnly)
                {
                    setAccessorStatements = SpecializedCollections.EmptyEnumerable<SyntaxNode>();
                }
            }

            accessors.Add(AccessorDeclaration(SyntaxKind.GetAccessorDeclaration, getAccessorStatements));

            if (hasSetter)
            {
                accessors.Add(AccessorDeclaration(SyntaxKind.SetAccessorDeclaration, setAccessorStatements));
            }

            return SyntaxFactory.IndexerDeclaration(
                default(SyntaxList<AttributeListSyntax>),
                AsModifierList(accessibility, modifiers - DeclarationModifiers.ReadOnly, SyntaxKind.IndexerDeclaration),
                (TypeSyntax)type,
                default(ExplicitInterfaceSpecifierSyntax),
                AsBracketedParameterList(parameters),
                SyntaxFactory.AccessorList(SyntaxFactory.List(accessors)));
        }

        private BracketedParameterListSyntax AsBracketedParameterList(IEnumerable<SyntaxNode> parameters)
        {
            return parameters != null
                ? SyntaxFactory.BracketedParameterList(SyntaxFactory.SeparatedList(parameters.Cast<ParameterSyntax>()))
                : SyntaxFactory.BracketedParameterList();
        }

        private AccessorDeclarationSyntax AccessorDeclaration(SyntaxKind kind, IEnumerable<SyntaxNode> statements)
        {
            var ad = SyntaxFactory.AccessorDeclaration(
                kind,
                statements != null ? CreateBlock(statements) : null);

            if (statements == null)
            {
                ad = ad.WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
            }

            return ad;
        }

        public override SyntaxNode EventDeclaration(
            string name, 
            SyntaxNode type, 
            Accessibility accessibility, 
            DeclarationModifiers modifiers)
        {
            return SyntaxFactory.EventFieldDeclaration(
                default(SyntaxList<AttributeListSyntax>),
                AsModifierList(accessibility, modifiers, SyntaxKind.EventFieldDeclaration),
                SyntaxFactory.VariableDeclaration(
                    (TypeSyntax)type,
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(name))));
        }

        public override SyntaxNode CustomEventDeclaration(
            string name, 
            SyntaxNode type, 
            Accessibility accessibility, 
            DeclarationModifiers modifiers, 
            IEnumerable<SyntaxNode> parameters,
            IEnumerable<SyntaxNode> addAccessorStatements, 
            IEnumerable<SyntaxNode> removeAccessorStatements)
        {
            var accessors = new List<AccessorDeclarationSyntax>();
            if (modifiers.IsAbstract)
            {
                addAccessorStatements = null;
                removeAccessorStatements = null;
            }
            else
            {
                if (addAccessorStatements == null)
                {
                    addAccessorStatements = SpecializedCollections.EmptyEnumerable<SyntaxNode>();
                }

                if (removeAccessorStatements == null)
                {
                    removeAccessorStatements = SpecializedCollections.EmptyEnumerable<SyntaxNode>();
                }
            }

            accessors.Add(AccessorDeclaration(SyntaxKind.AddAccessorDeclaration, addAccessorStatements));
            accessors.Add(AccessorDeclaration(SyntaxKind.RemoveAccessorDeclaration, removeAccessorStatements));

            return SyntaxFactory.EventDeclaration(
                default(SyntaxList<AttributeListSyntax>),
                AsModifierList(accessibility, modifiers, SyntaxKind.EventDeclaration),
                (TypeSyntax)type,
                default(ExplicitInterfaceSpecifierSyntax),
                name.ToIdentifierToken(),
                SyntaxFactory.AccessorList(SyntaxFactory.List(accessors)));
        }

        public override SyntaxNode AsPublicInterfaceImplementation(SyntaxNode declaration, SyntaxNode typeName)
        {
            // C# interface implementations are implicit/not-specified
            return PreserveTrivia(declaration, d => AsImplementation(d, Accessibility.Public));
        }

        public override SyntaxNode AsPrivateInterfaceImplementation(SyntaxNode declaration, SyntaxNode typeName)
        {
            return PreserveTrivia(declaration, d =>
            {
                var specifier = SyntaxFactory.ExplicitInterfaceSpecifier((NameSyntax)typeName);

                d = AsImplementation(d, Accessibility.NotApplicable);

                switch (d.CSharpKind())
                {
                    case SyntaxKind.MethodDeclaration:
                        return ((MethodDeclarationSyntax)d).WithExplicitInterfaceSpecifier(specifier);
                    case SyntaxKind.PropertyDeclaration:
                        return ((PropertyDeclarationSyntax)d).WithExplicitInterfaceSpecifier(specifier);
                    case SyntaxKind.IndexerDeclaration:
                        return ((IndexerDeclarationSyntax)d).WithExplicitInterfaceSpecifier(specifier);
                    case SyntaxKind.EventDeclaration:
                        return ((EventDeclarationSyntax)d).WithExplicitInterfaceSpecifier(specifier);
                }

                return d;
            });
        }

        private SyntaxNode AsImplementation(SyntaxNode declaration, Accessibility requiredAccess)
        {
            var mods = this.GetModifiers(declaration);
            declaration = this.WithAccessibility(declaration, requiredAccess);
            declaration = this.WithModifiers(declaration, this.GetModifiers(declaration) - DeclarationModifiers.Abstract);
            declaration = this.WithBodies(declaration);
            return declaration;
        }

        private SyntaxNode WithBodies(SyntaxNode declaration)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.MethodDeclaration:
                    var method = (MethodDeclarationSyntax)declaration;
                    return (method.Body == null) ? method.WithSemicolonToken(default(SyntaxToken)).WithBody(CreateBlock(null)) : method;
                case SyntaxKind.OperatorDeclaration:
                    var op = (OperatorDeclarationSyntax)declaration;
                    return op.Body == null ? op.WithSemicolonToken(default(SyntaxToken)).WithBody(CreateBlock(null)) : op;
                case SyntaxKind.ConversionOperatorDeclaration:
                    var cop = (ConversionOperatorDeclarationSyntax)declaration;
                    return cop.Body == null ? cop.WithSemicolonToken(default(SyntaxToken)).WithBody(CreateBlock(null)) : cop;
                case SyntaxKind.PropertyDeclaration:
                    var prop = (PropertyDeclarationSyntax)declaration;
                    return prop.WithAccessorList(WithBodies(prop.AccessorList));
                case SyntaxKind.IndexerDeclaration:
                    var ind = (IndexerDeclarationSyntax)declaration;
                    return ind.WithAccessorList(WithBodies(ind.AccessorList));
                case SyntaxKind.EventDeclaration:
                    var ev = (EventDeclarationSyntax)declaration;
                    return ev.WithAccessorList(WithBodies(ev.AccessorList));
            }

            return declaration;
        }

        private AccessorListSyntax WithBodies(AccessorListSyntax accessorList)
        {
            return accessorList.WithAccessors(SyntaxFactory.List(accessorList.Accessors.Select(a => WithBody(a))));
        }

        private AccessorDeclarationSyntax WithBody(AccessorDeclarationSyntax accessor)
        {
            if (accessor.Body == null)
            {
                return accessor.WithSemicolonToken(default(SyntaxToken)).WithBody(CreateBlock(null));
            }
            else
            {
                return accessor;
            }
        }

        private SyntaxNode WithoutBodies(SyntaxNode declaration)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.MethodDeclaration:
                    var method = (MethodDeclarationSyntax)declaration;
                    return (method.Body != null) ? method.WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)).WithBody(null) : method;
                case SyntaxKind.OperatorDeclaration:
                    var op = (OperatorDeclarationSyntax)declaration;
                    return op.Body != null ? op.WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)).WithBody(null) : op;
                case SyntaxKind.ConversionOperatorDeclaration:
                    var cop = (ConversionOperatorDeclarationSyntax)declaration;
                    return cop.Body == null ? cop.WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)).WithBody(null) : cop;
                case SyntaxKind.PropertyDeclaration:
                    var prop = (PropertyDeclarationSyntax)declaration;
                    return prop.WithAccessorList(WithoutBodies(prop.AccessorList));
                case SyntaxKind.IndexerDeclaration:
                    var ind = (IndexerDeclarationSyntax)declaration;
                    return ind.WithAccessorList(WithoutBodies(ind.AccessorList));
                case SyntaxKind.EventDeclaration:
                    var ev = (EventDeclarationSyntax)declaration;
                    return ev.WithAccessorList(WithoutBodies(ev.AccessorList));
            }

            return declaration;
        }

        private AccessorListSyntax WithoutBodies(AccessorListSyntax accessorList)
        {
            return accessorList.WithAccessors(SyntaxFactory.List(accessorList.Accessors.Select(WithoutBody)));
        }

        private AccessorDeclarationSyntax WithoutBody(AccessorDeclarationSyntax accessor)
        {
            return (accessor.Body != null) ? accessor.WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)).WithBody(null) : accessor;
        }

        public override SyntaxNode ClassDeclaration(
            string name,
            IEnumerable<string> typeParameters,
            Accessibility accessibility,
            DeclarationModifiers modifiers,
            SyntaxNode baseType,
            IEnumerable<SyntaxNode> interfaceTypes,
            IEnumerable<SyntaxNode> members)
        {
            List<BaseTypeSyntax> baseTypes = null;
            if (baseType != null || interfaceTypes != null)
            {
                baseTypes = new List<BaseTypeSyntax>();

                if (baseType != null)
                {
                    baseTypes.Add(SyntaxFactory.SimpleBaseType((TypeSyntax)baseType));
                }

                if (interfaceTypes != null)
                {
                    baseTypes.AddRange(interfaceTypes.Select(i => SyntaxFactory.SimpleBaseType((TypeSyntax)i)));
                }

                if (baseTypes.Count == 0)
                {
                    baseTypes = null;
                }
            }

            return SyntaxFactory.ClassDeclaration(
                default(SyntaxList<AttributeListSyntax>),
                AsModifierList(accessibility, modifiers, SyntaxKind.ClassDeclaration),
                name.ToIdentifierToken(),
                AsTypeParameterList(typeParameters),
                baseTypes != null ? SyntaxFactory.BaseList(SyntaxFactory.SeparatedList(baseTypes)) : null,
                default(SyntaxList<TypeParameterConstraintClauseSyntax>),
                AsClassMembers(name, members));
        }

        private SyntaxList<MemberDeclarationSyntax> AsClassMembers(string className, IEnumerable<SyntaxNode> members)
        {
            return members != null
                ? SyntaxFactory.List(members.Select(m => AsClassMember(m, className)).Where(m => m != null))
                : default(SyntaxList<MemberDeclarationSyntax>);
        }

        private MemberDeclarationSyntax AsClassMember(SyntaxNode node, string className)
        {
            switch (node.CSharpKind())
            {
                case SyntaxKind.ConstructorDeclaration:
                    node = ((ConstructorDeclarationSyntax)node).WithIdentifier(className.ToIdentifierToken());
                    break;

                case SyntaxKind.VariableDeclaration:
                case SyntaxKind.VariableDeclarator:
                    node = this.GetIsolatedDeclaration(node);
                    break;
            }

            return node as MemberDeclarationSyntax;
        }

        public override SyntaxNode StructDeclaration(
            string name,
            IEnumerable<string> typeParameters,
            Accessibility accessibility,
            DeclarationModifiers modifiers,
            IEnumerable<SyntaxNode> interfaceTypes,
            IEnumerable<SyntaxNode> members)
        {
            var itypes = interfaceTypes != null ? interfaceTypes.Select(i => (BaseTypeSyntax)SyntaxFactory.SimpleBaseType((TypeSyntax)i)).ToList() : null;
            if (itypes != null && itypes.Count == 0)
            {
                itypes = null;
            }

            return SyntaxFactory.StructDeclaration(
                default(SyntaxList<AttributeListSyntax>),
                AsModifierList(accessibility, modifiers, SyntaxKind.StructDeclaration),
                name.ToIdentifierToken(),
                AsTypeParameterList(typeParameters),
                itypes != null ? SyntaxFactory.BaseList(SyntaxFactory.SeparatedList(itypes)) : null,
                default(SyntaxList<TypeParameterConstraintClauseSyntax>),
                AsClassMembers(name, members));
        }

        public override SyntaxNode InterfaceDeclaration(
            string name,
            IEnumerable<string> typeParameters,
            Accessibility accessibility,
            IEnumerable<SyntaxNode> interfaceTypes = null,
            IEnumerable<SyntaxNode> members = null)
        {
            var itypes = interfaceTypes != null ? interfaceTypes.Select(i => (BaseTypeSyntax)SyntaxFactory.SimpleBaseType((TypeSyntax)i)).ToList() : null;
            if (itypes != null && itypes.Count == 0)
            {
                itypes = null;
            }

            return SyntaxFactory.InterfaceDeclaration(
                default(SyntaxList<AttributeListSyntax>),
                AsModifierList(accessibility, DeclarationModifiers.None),
                name.ToIdentifierToken(),
                AsTypeParameterList(typeParameters),
                itypes != null ? SyntaxFactory.BaseList(SyntaxFactory.SeparatedList(itypes)) : null,
                default(SyntaxList<TypeParameterConstraintClauseSyntax>),
                AsInterfaceMembers(members));
        }

        private SyntaxList<MemberDeclarationSyntax> AsInterfaceMembers(IEnumerable<SyntaxNode> members)
        {
            return members != null
                ? SyntaxFactory.List(members.Select(AsInterfaceMember).OfType<MemberDeclarationSyntax>())
                : default(SyntaxList<MemberDeclarationSyntax>);
        }

        private SyntaxNode AsInterfaceMember(SyntaxNode m)
        {
            return Isolate(m, member =>
            {
                Accessibility acc;
                DeclarationModifiers modifiers;

                switch (member.CSharpKind())
                {
                    case SyntaxKind.MethodDeclaration:
                        return ((MethodDeclarationSyntax)member)
                                 .WithModifiers(default(SyntaxTokenList))
                                 .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                                 .WithBody(null);

                    case SyntaxKind.PropertyDeclaration:
                        var property = (PropertyDeclarationSyntax)member;
                        return property
                            .WithModifiers(default(SyntaxTokenList))
                            .WithAccessorList(WithoutBodies(property.AccessorList));

                    case SyntaxKind.IndexerDeclaration:
                        var indexer = (IndexerDeclarationSyntax)member;
                        return indexer
                            .WithModifiers(default(SyntaxTokenList))
                            .WithAccessorList(WithoutBodies(indexer.AccessorList));

                    case SyntaxKind.EventDeclaration:
                        var ev = (EventDeclarationSyntax)member;
                        return ev
                            .WithModifiers(default(SyntaxTokenList))
                            .WithAccessorList(WithoutBodies(ev.AccessorList));

                    // convert event field into event
                    case SyntaxKind.EventFieldDeclaration:
                        var ef = (EventFieldDeclarationSyntax)member;
                        this.GetAccessibilityAndModifiers(ef.Modifiers, out acc, out modifiers);
                        var ep = this.CustomEventDeclaration(this.GetName(ef), this.GetType(ef), acc, modifiers, parameters: null, addAccessorStatements: null, removeAccessorStatements: null);
                        return this.AsInterfaceMember(ep);

                    // convert field into property
                    case SyntaxKind.FieldDeclaration:
                        var f = (FieldDeclarationSyntax)member;
                        this.GetAccessibilityAndModifiers(f.Modifiers, out acc, out modifiers);
                        return this.AsInterfaceMember(
                            this.PropertyDeclaration(this.GetName(f), this.GetType(f), acc, modifiers, getAccessorStatements: null, setAccessorStatements: null));

                    default:
                        return null;
                }
            });
        }

        public override SyntaxNode EnumDeclaration(
            string name, 
            Accessibility accessibility, 
            DeclarationModifiers modifiers,
            IEnumerable<SyntaxNode> members)
        {
            return SyntaxFactory.EnumDeclaration(
                default(SyntaxList<AttributeListSyntax>),
                AsModifierList(accessibility, modifiers, SyntaxKind.EnumDeclaration),
                name.ToIdentifierToken(),
                default(BaseListSyntax),
                AsEnumMembers(members));
        }

        public override SyntaxNode EnumMember(string name, SyntaxNode expression)
        {
            return SyntaxFactory.EnumMemberDeclaration(
                default(SyntaxList<AttributeListSyntax>),
                name.ToIdentifierToken(),
                expression != null ? SyntaxFactory.EqualsValueClause((ExpressionSyntax)expression) : null);
        }

        private EnumMemberDeclarationSyntax AsEnumMember(SyntaxNode node)
        {
            var ident = node as IdentifierNameSyntax;
            if (ident != null)
            {
                return (EnumMemberDeclarationSyntax)EnumMember(ident.Identifier.ToString(), null);
            }

            var field = node as FieldDeclarationSyntax;
            if (field != null && field.Declaration.Variables.Count == 1)
            {
                var variable = field.Declaration.Variables[0];
                return (EnumMemberDeclarationSyntax)EnumMember(variable.Identifier.ToString(), variable.Initializer);
            }

            return (EnumMemberDeclarationSyntax)node;
        }

        private SeparatedSyntaxList<EnumMemberDeclarationSyntax> AsEnumMembers(IEnumerable<SyntaxNode> members)
        {
            return members != null ? SyntaxFactory.SeparatedList(members.Select(AsEnumMember)) : default(SeparatedSyntaxList<EnumMemberDeclarationSyntax>);
        }

        public override SyntaxNode DelegateDeclaration(
            string name, 
            IEnumerable<SyntaxNode> parameters, 
            IEnumerable<string> typeParameters, 
            SyntaxNode returnType, 
            Accessibility accessibility = Accessibility.NotApplicable, 
            DeclarationModifiers modifiers = default(DeclarationModifiers))
        {
            return SyntaxFactory.DelegateDeclaration(
                default(SyntaxList<AttributeListSyntax>),
                AsModifierList(accessibility, modifiers),
                returnType != null ? (TypeSyntax)returnType : SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
                name.ToIdentifierToken(),
                AsTypeParameterList(typeParameters),
                AsParameterList(parameters),
                default(SyntaxList<TypeParameterConstraintClauseSyntax>));
        }

        public override SyntaxNode Attribute(SyntaxNode name, IEnumerable<SyntaxNode> attributeArguments)
        {
            return AsAttributeList(SyntaxFactory.Attribute((NameSyntax)name, AsAttributeArgumentList(attributeArguments)));
        }

        public override SyntaxNode AttributeArgument(string name, SyntaxNode expression)
        {
            return name != null
                ? SyntaxFactory.AttributeArgument(SyntaxFactory.NameEquals(name.ToIdentifierName()), default(NameColonSyntax), (ExpressionSyntax)expression)
                : SyntaxFactory.AttributeArgument((ExpressionSyntax)expression);
        }

        private AttributeArgumentListSyntax AsAttributeArgumentList(IEnumerable<SyntaxNode> arguments)
        {
            if (arguments != null)
            {
                return SyntaxFactory.AttributeArgumentList(SyntaxFactory.SeparatedList(arguments.Select(AsAttributeArgument)));
            }
            else
            {
                return null;
            }
        }

        private AttributeArgumentSyntax AsAttributeArgument(SyntaxNode node)
        {
            var expr = node as ExpressionSyntax;
            if (expr != null)
            {
                return SyntaxFactory.AttributeArgument(expr);
            }

            var arg = node as ArgumentSyntax;
            if (arg != null)
            {
                return SyntaxFactory.AttributeArgument(default(NameEqualsSyntax), arg.NameColon, arg.Expression);
            }

            return (AttributeArgumentSyntax)node;
        }

        protected override TNode ClearTrivia<TNode>(TNode node)
        {
            return node.WithLeadingTrivia(SyntaxFactory.ElasticSpace)
                       .WithTrailingTrivia(SyntaxFactory.ElasticSpace);
        }

        private SyntaxList<AttributeListSyntax> AsAttributeLists(IEnumerable<SyntaxNode> attributes)
        {
            if (attributes != null)
            {
                return SyntaxFactory.List(attributes.Select(AsAttributeList));
            }
            else
            {
                return default(SyntaxList<AttributeListSyntax>);
            }
        }

        private AttributeListSyntax AsAttributeList(SyntaxNode node)
        {
            var attr = node as AttributeSyntax;
            if (attr != null)
            {
                return SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attr));
            }
            else
            {
                return ((AttributeListSyntax)node).WithTarget(null);
            }
        }

        private static ConditionalWeakTable<SyntaxNode, IReadOnlyList<SyntaxNode>> declAttributes
            = new ConditionalWeakTable<SyntaxNode, IReadOnlyList<SyntaxNode>>();

        public override IReadOnlyList<SyntaxNode> GetAttributes(SyntaxNode declaration)
        {
            IReadOnlyList<SyntaxNode> attrs;
            if (!declAttributes.TryGetValue(declaration, out attrs))
            {
                var tmp = this.Flatten(GetAttributeLists(declaration).Where(al => !IsReturnAttribute(al)).ToImmutableReadOnlyListOrEmpty());
                attrs = declAttributes.GetValue(declaration, _d => tmp);
            }

            return attrs;
        }

        private static ConditionalWeakTable<SyntaxNode, IReadOnlyList<SyntaxNode>> declReturnAttributes
            = new ConditionalWeakTable<SyntaxNode, IReadOnlyList<SyntaxNode>>();

        public override IReadOnlyList<SyntaxNode> GetReturnAttributes(SyntaxNode declaration)
        {
            IReadOnlyList<SyntaxNode> attrs;
            if (!declReturnAttributes.TryGetValue(declaration, out attrs))
            {
                var tmp = this.Flatten(GetAttributeLists(declaration).Where(al => IsReturnAttribute(al)).ToImmutableReadOnlyListOrEmpty());
                attrs = declReturnAttributes.GetValue(declaration, _d => tmp);
            }

            return attrs;
        }

        static bool IsReturnAttribute(AttributeListSyntax list)
        {
            return list.Target != null ? list.Target.Identifier.IsKind(SyntaxKind.ReturnKeyword) : false;
        }

        public override SyntaxNode InsertAttributes(SyntaxNode declaration, int index, IEnumerable<SyntaxNode> attributes)
        {
            return Isolate(declaration, d => InsertAttributesInternal(d, index, attributes));
        }

        private SyntaxNode InsertAttributesInternal(SyntaxNode declaration, int index, IEnumerable<SyntaxNode> attributes)
        {
            var newAttributes = AsAttributeLists(ClearTrivia(attributes));

            var existingAttributes = this.GetAttributes(declaration);
            if (index >= 0 && index < existingAttributes.Count)
            {
                return this.InsertDeclarationsBefore(declaration, existingAttributes[index], newAttributes);
            }
            else
            {
                var lists = GetAttributeLists(declaration);

                if (index > 0)
                {
                    index = lists.Count;
                }

                var newList = lists.InsertRange(index, newAttributes);
                return WithAttributeLists(declaration, newList);
            }
        }

        public override SyntaxNode InsertReturnAttributes(SyntaxNode declaration, int index, IEnumerable<SyntaxNode> attributes)
        {
            if (!declaration.IsKind(SyntaxKind.MethodDeclaration))
            {
                return declaration;
            }

            return Isolate(declaration, d => InsertReturnAttributesInternal(d, index, attributes));
        }

        private SyntaxNode InsertReturnAttributesInternal(SyntaxNode d, int index, IEnumerable<SyntaxNode> attributes)
        {
            var newAttributes = AsReturnAttributes(ClearTrivia(attributes));

            var existingAttributes = this.GetReturnAttributes(d);
            if (index >= 0 && index < existingAttributes.Count)
            {
                return this.InsertDeclarationsBefore(d, existingAttributes[index], newAttributes);
            }
            else
            {
                var lists = GetAttributeLists(d);

                if (index > 0)
                {
                    index = lists.Count;
                }

                var newList = lists.InsertRange(index, newAttributes);
                return WithAttributeLists(d, newList);
            }
        }

        private IEnumerable<AttributeListSyntax> AsReturnAttributes(IEnumerable<SyntaxNode> attributes)
        {
            return AsAttributeLists(attributes)
                .Select(list => list.WithTarget(SyntaxFactory.AttributeTargetSpecifier(SyntaxFactory.Token(SyntaxKind.ReturnKeyword))));
        }

        private SyntaxList<AttributeListSyntax> AsAssemblyAttributes(IEnumerable<AttributeListSyntax> attributes)
        {
            return SyntaxFactory.List(
                    attributes.Select(list => list.WithTarget(SyntaxFactory.AttributeTargetSpecifier(SyntaxFactory.Token(SyntaxKind.AssemblyKeyword)))));
        }

        public override IReadOnlyList<SyntaxNode> GetAttributeArguments(SyntaxNode attributeDeclaration)
        {
            switch (attributeDeclaration.CSharpKind())
            {
                case SyntaxKind.AttributeList:
                    var list = (AttributeListSyntax)attributeDeclaration;
                    if (list.Attributes.Count == 1)
                    {
                        return GetAttributeArguments(list.Attributes[0]);
                    }
                    break;
                case SyntaxKind.Attribute:
                    var attr = (AttributeSyntax)attributeDeclaration;
                    if (attr.ArgumentList != null)
                    {
                        return attr.ArgumentList.Arguments;
                    }
                    break;
            }

            return SpecializedCollections.EmptyReadOnlyList<SyntaxNode>();
        }

        public override SyntaxNode InsertAttributeArguments(SyntaxNode declaration, int index, IEnumerable<SyntaxNode> attributeArguments)
        {
            var newArgumentList = this.AsAttributeArgumentList(this.ClearTrivia(attributeArguments));

            var existingArgumentList = this.GetAttributeArgumentList(declaration);

            if (existingArgumentList == null)
            {
                return this.WithAttributeArgumentList(declaration, newArgumentList);
            }
            else if (newArgumentList != null)
            {
                return this.WithAttributeArgumentList(declaration, existingArgumentList.WithArguments(existingArgumentList.Arguments.InsertRange(index, newArgumentList.Arguments)));
            }
            else
            {
                return declaration;
            }
        }

        private AttributeArgumentListSyntax GetAttributeArgumentList(SyntaxNode declaration)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.AttributeList:
                    var list = (AttributeListSyntax)declaration;
                    if (list.Attributes.Count == 1)
                    {
                        return list.Attributes[0].ArgumentList;
                    }
                    break;
                case SyntaxKind.Attribute:
                    var attr = (AttributeSyntax)declaration;
                    return attr.ArgumentList;
            }

            return null;
        }

        private SyntaxNode WithAttributeArgumentList(SyntaxNode declaration, AttributeArgumentListSyntax argList)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.AttributeList:
                    var list = (AttributeListSyntax)declaration;
                    if (list.Attributes.Count == 1)
                    {
                        return ReplaceWithTrivia(declaration, list.Attributes[0], list.Attributes[0].WithArgumentList(argList));
                    }
                    break;
                case SyntaxKind.Attribute:
                    var attr = (AttributeSyntax)declaration;
                    return attr.WithArgumentList(argList);
            }

            return declaration;
        }

        private SyntaxList<AttributeListSyntax> GetAttributeLists(SyntaxNode declaration)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.ClassDeclaration:
                    return ((ClassDeclarationSyntax)declaration).AttributeLists;
                case SyntaxKind.StructDeclaration:
                    return ((StructDeclarationSyntax)declaration).AttributeLists;
                case SyntaxKind.InterfaceDeclaration:
                    return ((InterfaceDeclarationSyntax)declaration).AttributeLists;
                case SyntaxKind.EnumDeclaration:
                    return ((EnumDeclarationSyntax)declaration).AttributeLists;
                case SyntaxKind.DelegateDeclaration:
                    return ((DelegateDeclarationSyntax)declaration).AttributeLists;
                case SyntaxKind.MethodDeclaration:
                    return ((MethodDeclarationSyntax)declaration).AttributeLists;
                case SyntaxKind.OperatorDeclaration:
                    return ((OperatorDeclarationSyntax)declaration).AttributeLists;
                case SyntaxKind.ConversionOperatorDeclaration:
                    return ((ConversionOperatorDeclarationSyntax)declaration).AttributeLists;
                case SyntaxKind.ConstructorDeclaration:
                    return ((ConstructorDeclarationSyntax)declaration).AttributeLists;
                case SyntaxKind.DestructorDeclaration:
                    return ((DestructorDeclarationSyntax)declaration).AttributeLists;
                case SyntaxKind.FieldDeclaration:
                    return ((FieldDeclarationSyntax)declaration).AttributeLists;
                case SyntaxKind.PropertyDeclaration:
                    return ((PropertyDeclarationSyntax)declaration).AttributeLists;
                case SyntaxKind.EventFieldDeclaration:
                    return ((EventFieldDeclarationSyntax)declaration).AttributeLists;
                case SyntaxKind.EventDeclaration:
                    return ((EventDeclarationSyntax)declaration).AttributeLists;
                case SyntaxKind.Parameter:
                    return ((ParameterSyntax)declaration).AttributeLists;
                case SyntaxKind.CompilationUnit:
                    return ((CompilationUnitSyntax)declaration).AttributeLists;
                default:
                    return default(SyntaxList<AttributeListSyntax>);
            }
        }

        private SyntaxNode WithAttributeLists(SyntaxNode declaration, SyntaxList<AttributeListSyntax> attributeLists)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.ClassDeclaration:
                    return ((ClassDeclarationSyntax)declaration).WithAttributeLists(attributeLists);
                case SyntaxKind.StructDeclaration:
                    return ((StructDeclarationSyntax)declaration).WithAttributeLists(attributeLists);
                case SyntaxKind.InterfaceDeclaration:
                    return ((InterfaceDeclarationSyntax)declaration).WithAttributeLists(attributeLists);
                case SyntaxKind.EnumDeclaration:
                    return ((EnumDeclarationSyntax)declaration).WithAttributeLists(attributeLists);
                case SyntaxKind.DelegateDeclaration:
                    return ((DelegateDeclarationSyntax)declaration).WithAttributeLists(attributeLists);
                case SyntaxKind.MethodDeclaration:
                    return ((MethodDeclarationSyntax)declaration).WithAttributeLists(attributeLists);
                case SyntaxKind.OperatorDeclaration:
                    return ((OperatorDeclarationSyntax)declaration).WithAttributeLists(attributeLists);
                case SyntaxKind.ConversionOperatorDeclaration:
                    return ((ConversionOperatorDeclarationSyntax)declaration).WithAttributeLists(attributeLists);
                case SyntaxKind.ConstructorDeclaration:
                    return ((ConstructorDeclarationSyntax)declaration).WithAttributeLists(attributeLists);
                case SyntaxKind.DestructorDeclaration:
                    return ((DestructorDeclarationSyntax)declaration).WithAttributeLists(attributeLists);
                case SyntaxKind.FieldDeclaration:
                    return ((FieldDeclarationSyntax)declaration).WithAttributeLists(attributeLists);
                case SyntaxKind.PropertyDeclaration:
                    return ((PropertyDeclarationSyntax)declaration).WithAttributeLists(attributeLists);
                case SyntaxKind.IndexerDeclaration:
                    return ((IndexerDeclarationSyntax)declaration).WithAttributeLists(attributeLists);
                case SyntaxKind.EventFieldDeclaration:
                    return ((EventFieldDeclarationSyntax)declaration).WithAttributeLists(attributeLists);
                case SyntaxKind.EventDeclaration:
                    return ((EventDeclarationSyntax)declaration).WithAttributeLists(attributeLists);
                case SyntaxKind.Parameter:
                    return ((ParameterSyntax)declaration).WithAttributeLists(attributeLists);
                case SyntaxKind.CompilationUnit:
                    return ((CompilationUnitSyntax)declaration).WithAttributeLists(AsAssemblyAttributes(attributeLists));
                default:
                    return declaration;
            }
        }

        public override IReadOnlyList<SyntaxNode> GetNamespaceImports(SyntaxNode declaration)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.CompilationUnit:
                    return ((CompilationUnitSyntax)declaration).Usings;
                case SyntaxKind.NamespaceDeclaration:
                    return ((NamespaceDeclarationSyntax)declaration).Usings;
                default:
                    return SpecializedCollections.EmptyReadOnlyList<SyntaxNode>();
            }
        }

        public override SyntaxNode InsertNamespaceImports(SyntaxNode declaration, int index, IEnumerable<SyntaxNode> imports)
        {
            var usings = AsUsingDirectives(this.ClearTrivia(imports));

            switch (declaration.CSharpKind())
            {
                case SyntaxKind.CompilationUnit:
                    var cu = ((CompilationUnitSyntax)declaration);
                    return cu.WithUsings(cu.Usings.InsertRange(index, usings));
                case SyntaxKind.NamespaceDeclaration:
                    var nd = ((NamespaceDeclarationSyntax)declaration);
                    return nd.WithUsings(nd.Usings.InsertRange(index, usings));
                default:
                    return declaration;
            }
        }

        public override IReadOnlyList<SyntaxNode> GetMembers(SyntaxNode declaration)
        {
            return Flatten(GetUnflattenedMembers(declaration));
        }

        private IReadOnlyList<SyntaxNode> GetUnflattenedMembers(SyntaxNode declaration)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.ClassDeclaration:
                    return ((ClassDeclarationSyntax)declaration).Members;
                case SyntaxKind.StructDeclaration:
                    return ((StructDeclarationSyntax)declaration).Members;
                case SyntaxKind.InterfaceDeclaration:
                    return ((InterfaceDeclarationSyntax)declaration).Members;
                case SyntaxKind.EnumDeclaration:
                    return ((EnumDeclarationSyntax)declaration).Members;
                case SyntaxKind.NamespaceDeclaration:
                    return ((NamespaceDeclarationSyntax)declaration).Members;
                case SyntaxKind.CompilationUnit:
                    return ((CompilationUnitSyntax)declaration).Members;
                default:
                    return SpecializedCollections.EmptyReadOnlyList<SyntaxNode>();
            }
        }

        private IReadOnlyList<SyntaxNode> Flatten(IReadOnlyList<SyntaxNode> members)
        {
            if (members.Count == 0 || !members.Any(m => GetDeclarationCount(m) > 1))
            {
                return members;
            }

            var list = new List<SyntaxNode>();

            foreach (var declaration in members)
            {
                switch (declaration.CSharpKind())
                {
                    case SyntaxKind.FieldDeclaration:
                        Flatten(declaration, ((FieldDeclarationSyntax)declaration).Declaration, list);
                        break;
                    case SyntaxKind.EventFieldDeclaration:
                        Flatten(declaration, ((EventFieldDeclarationSyntax)declaration).Declaration, list);
                        break;
                    case SyntaxKind.LocalDeclarationStatement:
                        Flatten(declaration, ((LocalDeclarationStatementSyntax)declaration).Declaration, list);
                        break;
                    case SyntaxKind.VariableDeclaration:
                        Flatten(declaration, (VariableDeclarationSyntax)declaration, list);
                        break;
                    case SyntaxKind.AttributeList:
                        var attrList = (AttributeListSyntax)declaration;
                        if (attrList.Attributes.Count > 1)
                        {
                            list.AddRange(attrList.Attributes);
                        }
                        else
                        {
                            list.Add(attrList);
                        }
                        break;
                    default:
                        list.Add(declaration);
                        break;
                }
            }

            return list.ToImmutableReadOnlyListOrEmpty();
        }

        private static int GetDeclarationCount(SyntaxNode declaration)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.FieldDeclaration:
                    return ((FieldDeclarationSyntax)declaration).Declaration.Variables.Count;
                case SyntaxKind.EventFieldDeclaration:
                    return ((EventFieldDeclarationSyntax)declaration).Declaration.Variables.Count;
                case SyntaxKind.LocalDeclarationStatement:
                    return ((LocalDeclarationStatementSyntax)declaration).Declaration.Variables.Count;
                case SyntaxKind.VariableDeclaration:
                    return ((VariableDeclarationSyntax)declaration).Variables.Count;
                case SyntaxKind.AttributeList:
                    return ((AttributeListSyntax)declaration).Attributes.Count;
                default:
                    return 1;
            }
        }

        private void Flatten(SyntaxNode declaration, VariableDeclarationSyntax vd, List<SyntaxNode> flat)
        {
            if (vd.Variables.Count > 1)
            {
                flat.AddRange(vd.Variables);
            }
            else
            {
                flat.Add(declaration);
            }
        }

        private int GetActualMemberIndex(SyntaxNode declaration, int index)
        {
            int logicalIndex = 0;
            int actualIndex = 0;

            foreach (var m in this.GetUnflattenedMembers(declaration))
            {
                var count = GetDeclarationCount(m);

                if (index >= logicalIndex && index < logicalIndex + count)
                {
                    return actualIndex;
                }

                logicalIndex += count;
                actualIndex++;
            }

            return actualIndex;
        }

        public override SyntaxNode InsertMembers(SyntaxNode declaration, int index, IEnumerable<SyntaxNode> members)
        {
            members = this.AsMembersOf(declaration, this.ClearTrivia(members));

            var existingMembers = this.GetMembers(declaration);
            if (index >= 0 && index < existingMembers.Count)
            {
                return this.InsertDeclarationsBefore(declaration, existingMembers[index], members);
            }
            else
            {
                var actualIndex = this.GetActualMemberIndex(declaration, index);

                switch (declaration.CSharpKind())
                {
                    case SyntaxKind.ClassDeclaration:
                        var cd = ((ClassDeclarationSyntax)declaration);
                        return cd.WithMembers(cd.Members.InsertRange(actualIndex, this.AsClassMembers(cd.Identifier.Text, members)));
                    case SyntaxKind.StructDeclaration:
                        var sd = ((StructDeclarationSyntax)declaration);
                        return sd.WithMembers(sd.Members.InsertRange(actualIndex, this.AsClassMembers(sd.Identifier.Text, members)));
                    case SyntaxKind.InterfaceDeclaration:
                        var id = ((InterfaceDeclarationSyntax)declaration);
                        return id.WithMembers(id.Members.InsertRange(actualIndex, this.AsInterfaceMembers(members)));
                    case SyntaxKind.EnumDeclaration:
                        var ed = ((EnumDeclarationSyntax)declaration);
                        return ed.WithMembers(ed.Members.InsertRange(actualIndex, this.AsEnumMembers(members)));
                    case SyntaxKind.NamespaceDeclaration:
                        var nd = ((NamespaceDeclarationSyntax)declaration);
                        return nd.WithMembers(nd.Members.InsertRange(actualIndex, this.AsNamespaceMembers(members)));
                    case SyntaxKind.CompilationUnit:
                        var cu = ((CompilationUnitSyntax)declaration);
                        return cu.WithMembers(cu.Members.InsertRange(actualIndex, this.AsNamespaceMembers(members)));
                    default:
                        return declaration;
                }
            }
        }

        private IEnumerable<MemberDeclarationSyntax> AsMembersOf(SyntaxNode declaration, IEnumerable<SyntaxNode> members)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.ClassDeclaration:
                    var cd = ((ClassDeclarationSyntax)declaration);
                    return this.AsClassMembers(cd.Identifier.Text, members);
                case SyntaxKind.StructDeclaration:
                    var sd = ((StructDeclarationSyntax)declaration);
                    return this.AsClassMembers(sd.Identifier.Text, members);
                case SyntaxKind.InterfaceDeclaration:
                    var id = ((InterfaceDeclarationSyntax)declaration);
                    return this.AsInterfaceMembers(members);
                case SyntaxKind.EnumDeclaration:
                    var ed = ((EnumDeclarationSyntax)declaration);
                    return this.AsEnumMembers(members);
                case SyntaxKind.NamespaceDeclaration:
                    var nd = ((NamespaceDeclarationSyntax)declaration);
                    return this.AsNamespaceMembers(members);
                case SyntaxKind.CompilationUnit:
                    var cu = ((CompilationUnitSyntax)declaration);
                    return this.AsNamespaceMembers(members);
                default:
                    return SpecializedCollections.EmptyEnumerable<MemberDeclarationSyntax>();
            }
        }

        private bool CanHaveAccessibility(SyntaxNode declaration)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.StructDeclaration:
                case SyntaxKind.InterfaceDeclaration:
                case SyntaxKind.EnumDeclaration:
                case SyntaxKind.DelegateDeclaration:
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.OperatorDeclaration:
                case SyntaxKind.ConversionOperatorDeclaration:
                case SyntaxKind.ConstructorDeclaration:
                case SyntaxKind.FieldDeclaration:
                case SyntaxKind.PropertyDeclaration:
                case SyntaxKind.IndexerDeclaration:
                case SyntaxKind.EventFieldDeclaration:
                case SyntaxKind.EventDeclaration:
                    return true;
                case SyntaxKind.VariableDeclaration:
                case SyntaxKind.VariableDeclarator:
                    return GetDeclarationKind(declaration) == DeclarationKind.Field;
                case SyntaxKind.EnumMemberDeclaration:
                case SyntaxKind.Parameter:
                case SyntaxKind.LocalDeclarationStatement:
                case SyntaxKind.DestructorDeclaration:
                default:
                    return false;
            }
        }

        public override Accessibility GetAccessibility(SyntaxNode declaration)
        {
            if (!CanHaveAccessibility(declaration))
            {
                return Accessibility.NotApplicable;
            }

            var modifierTokens = GetModifierList(declaration);
            Accessibility accessibility;
            DeclarationModifiers modifiers;
            GetAccessibilityAndModifiers(modifierTokens, out accessibility, out modifiers);
            return accessibility;
        }

        public override SyntaxNode WithAccessibility(SyntaxNode declaration, Accessibility accessibility)
        {
            if (!CanHaveAccessibility(declaration))
            {
                return declaration;
            }

            return Isolate(declaration, d =>
            {
                var tokens = GetModifierList(d);
                Accessibility tmp;
                DeclarationModifiers modifiers;
                this.GetAccessibilityAndModifiers(tokens, out tmp, out modifiers);
                var newTokens = this.Merge(tokens, AsModifierList(accessibility, modifiers));
                return SetModifierList(d, newTokens);
            });
        }

        private static DeclarationModifiers fieldModifiers = DeclarationModifiers.Const | DeclarationModifiers.New | DeclarationModifiers.ReadOnly | DeclarationModifiers.Static;
        private static DeclarationModifiers methodModifiers = DeclarationModifiers.Abstract | DeclarationModifiers.Async | DeclarationModifiers.New | DeclarationModifiers.Override | DeclarationModifiers.Partial | DeclarationModifiers.Sealed | DeclarationModifiers.Static | DeclarationModifiers.Virtual;
        private static DeclarationModifiers constructorModifers = DeclarationModifiers.Static;
        private static DeclarationModifiers propertyModifiers = DeclarationModifiers.Abstract | DeclarationModifiers.New | DeclarationModifiers.Override | DeclarationModifiers.ReadOnly | DeclarationModifiers.Sealed | DeclarationModifiers.Static | DeclarationModifiers.Virtual;
        private static DeclarationModifiers eventModifiers = DeclarationModifiers.Abstract | DeclarationModifiers.New | DeclarationModifiers.Override | DeclarationModifiers.Sealed | DeclarationModifiers.Static | DeclarationModifiers.Virtual;
        private static DeclarationModifiers eventFieldModifiers = DeclarationModifiers.New | DeclarationModifiers.Static;
        private static DeclarationModifiers indexerModifiers = DeclarationModifiers.Abstract | DeclarationModifiers.New | DeclarationModifiers.Override | DeclarationModifiers.ReadOnly | DeclarationModifiers.Sealed | DeclarationModifiers.Static | DeclarationModifiers.Virtual;
        private static DeclarationModifiers classModifiers = DeclarationModifiers.Abstract | DeclarationModifiers.New | DeclarationModifiers.Partial | DeclarationModifiers.Sealed | DeclarationModifiers.Static;
        private static DeclarationModifiers structModifiers = DeclarationModifiers.New | DeclarationModifiers.Partial;
        private static DeclarationModifiers interfaceModifiers = DeclarationModifiers.New | DeclarationModifiers.Partial;

        private DeclarationModifiers GetAllowedModifiers(SyntaxKind kind)
        {
            switch (kind)
            {
                case SyntaxKind.ClassDeclaration:
                    return classModifiers;

                case SyntaxKind.EnumDeclaration:
                    return DeclarationModifiers.New;

                case SyntaxKind.DelegateDeclaration:
                    return DeclarationModifiers.New;

                case SyntaxKind.InterfaceDeclaration:
                    return interfaceModifiers;

                case SyntaxKind.StructDeclaration:
                    return structModifiers;

                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.OperatorDeclaration:
                case SyntaxKind.ConversionOperatorDeclaration:
                    return methodModifiers;

                case SyntaxKind.ConstructorDeclaration:
                    return constructorModifers;

                case SyntaxKind.FieldDeclaration:
                    return fieldModifiers;

                case SyntaxKind.PropertyDeclaration:
                    return propertyModifiers;

                case SyntaxKind.IndexerDeclaration:
                    return indexerModifiers;

                case SyntaxKind.EventFieldDeclaration:
                    return eventFieldModifiers;

                case SyntaxKind.EventDeclaration:
                    return eventModifiers;

                case SyntaxKind.EnumMemberDeclaration:
                case SyntaxKind.Parameter:
                case SyntaxKind.LocalDeclarationStatement:
                case SyntaxKind.DestructorDeclaration:
                default:
                    return DeclarationModifiers.None;
            }
        }

        public override DeclarationModifiers GetModifiers(SyntaxNode declaration)
        {
            var modifierTokens = GetModifierList(declaration);
            Accessibility accessibility;
            DeclarationModifiers modifiers;
            GetAccessibilityAndModifiers(modifierTokens, out accessibility, out modifiers);
            return modifiers;
        }

        public override SyntaxNode WithModifiers(SyntaxNode declaration, DeclarationModifiers modifiers)
        {
            modifiers = modifiers & GetAllowedModifiers(declaration.CSharpKind());
            var existingModifiers = GetModifiers(declaration);

            if (modifiers != existingModifiers)
            {
                return Isolate(declaration, d =>
                {
                    var tokens = GetModifierList(d);
                    Accessibility accessibility;
                    DeclarationModifiers tmp;
                    this.GetAccessibilityAndModifiers(tokens, out accessibility, out tmp);
                    var newTokens = this.Merge(tokens, AsModifierList(accessibility, modifiers));
                    return SetModifierList(d, newTokens);
                });
            }
            else
            {
                // no change
                return declaration;
            }
        }

        private SyntaxTokenList GetModifierList(SyntaxNode declaration)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.ClassDeclaration:
                    return ((ClassDeclarationSyntax)declaration).Modifiers;
                case SyntaxKind.StructDeclaration:
                    return ((StructDeclarationSyntax)declaration).Modifiers;
                case SyntaxKind.InterfaceDeclaration:
                    return ((InterfaceDeclarationSyntax)declaration).Modifiers;
                case SyntaxKind.EnumDeclaration:
                    return ((EnumDeclarationSyntax)declaration).Modifiers;
                case SyntaxKind.DelegateDeclaration:
                    return ((DelegateDeclarationSyntax)declaration).Modifiers;
                case SyntaxKind.MethodDeclaration:
                    return ((MethodDeclarationSyntax)declaration).Modifiers;
                case SyntaxKind.OperatorDeclaration:
                    return ((OperatorDeclarationSyntax)declaration).Modifiers;
                case SyntaxKind.ConversionOperatorDeclaration:
                    return ((ConversionOperatorDeclarationSyntax)declaration).Modifiers;
                case SyntaxKind.ConstructorDeclaration:
                    return ((ConstructorDeclarationSyntax)declaration).Modifiers;
                case SyntaxKind.DestructorDeclaration:
                    return ((DestructorDeclarationSyntax)declaration).Modifiers;
                case SyntaxKind.FieldDeclaration:
                    return ((FieldDeclarationSyntax)declaration).Modifiers;
                case SyntaxKind.PropertyDeclaration:
                    return ((PropertyDeclarationSyntax)declaration).Modifiers;
                case SyntaxKind.IndexerDeclaration:
                    return ((IndexerDeclarationSyntax)declaration).Modifiers;
                case SyntaxKind.EventFieldDeclaration:
                    return ((EventFieldDeclarationSyntax)declaration).Modifiers;
                case SyntaxKind.EventDeclaration:
                    return ((EventDeclarationSyntax)declaration).Modifiers;
                case SyntaxKind.Parameter:
                    return ((ParameterSyntax)declaration).Modifiers;
                case SyntaxKind.LocalDeclarationStatement:
                    return ((LocalDeclarationStatementSyntax)declaration).Modifiers;
                case SyntaxKind.VariableDeclaration:
                case SyntaxKind.VariableDeclarator:
                    if (declaration.Parent != null)
                    {
                        return GetModifierList(declaration.Parent);
                    }
                    break;
            }

            return default(SyntaxTokenList);
        }

        private SyntaxNode SetModifierList(SyntaxNode declaration, SyntaxTokenList modifiers)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.ClassDeclaration:
                    return ((ClassDeclarationSyntax)declaration).WithModifiers(modifiers);
                case SyntaxKind.StructDeclaration:
                    return ((StructDeclarationSyntax)declaration).WithModifiers(modifiers);
                case SyntaxKind.InterfaceDeclaration:
                    return ((InterfaceDeclarationSyntax)declaration).WithModifiers(modifiers);
                case SyntaxKind.EnumDeclaration:
                    return ((EnumDeclarationSyntax)declaration).WithModifiers(modifiers);
                case SyntaxKind.DelegateDeclaration:
                    return ((DelegateDeclarationSyntax)declaration).WithModifiers(modifiers);
                case SyntaxKind.MethodDeclaration:
                    return ((MethodDeclarationSyntax)declaration).WithModifiers(modifiers);
                case SyntaxKind.OperatorDeclaration:
                    return ((OperatorDeclarationSyntax)declaration).WithModifiers(modifiers);
                case SyntaxKind.ConversionOperatorDeclaration:
                    return ((ConversionOperatorDeclarationSyntax)declaration).WithModifiers(modifiers);
                case SyntaxKind.ConstructorDeclaration:
                    return ((ConstructorDeclarationSyntax)declaration).WithModifiers(modifiers);
                case SyntaxKind.DestructorDeclaration:
                    return ((DestructorDeclarationSyntax)declaration).WithModifiers(modifiers);
                case SyntaxKind.FieldDeclaration:
                    return ((FieldDeclarationSyntax)declaration).WithModifiers(modifiers);
                case SyntaxKind.PropertyDeclaration:
                    return ((PropertyDeclarationSyntax)declaration).WithModifiers(modifiers);
                case SyntaxKind.IndexerDeclaration:
                    return ((IndexerDeclarationSyntax)declaration).WithModifiers(modifiers);
                case SyntaxKind.EventFieldDeclaration:
                    return ((EventFieldDeclarationSyntax)declaration).WithModifiers(modifiers);
                case SyntaxKind.EventDeclaration:
                    return ((EventDeclarationSyntax)declaration).WithModifiers(modifiers);
                case SyntaxKind.Parameter:
                    return ((ParameterSyntax)declaration).WithModifiers(modifiers);
                case SyntaxKind.LocalDeclarationStatement:
                    return ((LocalDeclarationStatementSyntax)declaration).WithModifiers(modifiers);
                default:
                    return declaration;
            }
        }

        private SyntaxTokenList AsModifierList(Accessibility accessibility, DeclarationModifiers modifiers, SyntaxKind kind)
        {
            return AsModifierList(accessibility, GetAllowedModifiers(kind) & modifiers);
        }

        private SyntaxTokenList AsModifierList(Accessibility accessibility, DeclarationModifiers modifiers)
        {
            SyntaxTokenList list = SyntaxFactory.TokenList();

            switch (accessibility)
            {
                case Accessibility.Internal:
                    list = list.Add(SyntaxFactory.Token(SyntaxKind.InternalKeyword));
                    break;
                case Accessibility.Public:
                    list = list.Add(SyntaxFactory.Token(SyntaxKind.PublicKeyword));
                    break;
                case Accessibility.Private:
                    list = list.Add(SyntaxFactory.Token(SyntaxKind.PrivateKeyword));
                    break;
                case Accessibility.Protected:
                    list = list.Add(SyntaxFactory.Token(SyntaxKind.ProtectedKeyword));
                    break;
                case Accessibility.ProtectedOrInternal:
                    list = list.Add(SyntaxFactory.Token(SyntaxKind.InternalKeyword))
                               .Add(SyntaxFactory.Token(SyntaxKind.ProtectedKeyword));
                    break;
                case Accessibility.NotApplicable:
                    break;
            }

            if (modifiers.IsAbstract)
            {
                list = list.Add(SyntaxFactory.Token(SyntaxKind.AbstractKeyword));
            }

            if (modifiers.IsNew)
            {
                list = list.Add(SyntaxFactory.Token(SyntaxKind.NewKeyword));
            }

            if (modifiers.IsOverride)
            {
                list = list.Add(SyntaxFactory.Token(SyntaxKind.OverrideKeyword));
            }

            if (modifiers.IsVirtual)
            {
                list = list.Add(SyntaxFactory.Token(SyntaxKind.VirtualKeyword));
            }

            if (modifiers.IsStatic)
            {
                list = list.Add(SyntaxFactory.Token(SyntaxKind.StaticKeyword));
            }

            if (modifiers.IsAsync)
            {
                list = list.Add(SyntaxFactory.Token(SyntaxKind.AsyncKeyword));
            }

            if (modifiers.IsConst)
            {
                list = list.Add(SyntaxFactory.Token(SyntaxKind.ConstKeyword));
            }

            if (modifiers.IsReadOnly)
            {
                list = list.Add(SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword));
            }

            if (modifiers.IsSealed)
            {
                list = list.Add(SyntaxFactory.Token(SyntaxKind.SealedKeyword));
            }

            if (modifiers.IsUnsafe)
            {
                list = list.Add(SyntaxFactory.Token(SyntaxKind.UnsafeKeyword));
            }

            // partial must be last
            if (modifiers.IsPartial)
            {
                list = list.Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword));
            }

            return list;
        }

        private SyntaxTokenList Merge(SyntaxTokenList original, SyntaxTokenList newList)
        {
            // return tokens from newList, but use original tokens of kind matches
            return SyntaxFactory.TokenList(newList.Select(token => original.Any(token.CSharpKind()) ? original.First(tk => tk.IsKind(token.CSharpKind())) : token));
        }

        private void GetAccessibilityAndModifiers(SyntaxTokenList modifierList, out Accessibility accessibility, out DeclarationModifiers modifiers)
        { 
            accessibility = Accessibility.NotApplicable;
            modifiers = DeclarationModifiers.None;

            foreach (var token in modifierList)
            {
                switch (token.CSharpKind())
                {
                    case SyntaxKind.PublicKeyword:
                        accessibility = Accessibility.Public;
                        break;

                    case SyntaxKind.PrivateKeyword:
                        accessibility = Accessibility.Private;
                        break;

                    case SyntaxKind.InternalKeyword:
                        if (accessibility == Accessibility.Protected)
                        {
                            accessibility = Accessibility.ProtectedOrInternal;
                        }
                        else
                        {
                            accessibility = Accessibility.Internal;
                        }

                        break;

                    case SyntaxKind.ProtectedKeyword:
                        if (accessibility == Accessibility.Internal)
                        {
                            accessibility = Accessibility.ProtectedOrInternal;
                        }
                        else
                        {
                            accessibility = Accessibility.Protected;
                        }

                        break;

                    case SyntaxKind.AbstractKeyword:
                        modifiers = modifiers | DeclarationModifiers.Abstract;
                        break;

                    case SyntaxKind.NewKeyword:
                        modifiers = modifiers | DeclarationModifiers.New;
                        break;

                    case SyntaxKind.OverrideKeyword:
                        modifiers = modifiers | DeclarationModifiers.Override;
                        break;

                    case SyntaxKind.VirtualKeyword:
                        modifiers = modifiers | DeclarationModifiers.Virtual;
                        break;

                    case SyntaxKind.StaticKeyword:
                        modifiers = modifiers | DeclarationModifiers.Static;
                        break;

                    case SyntaxKind.AsyncKeyword:
                        modifiers = modifiers | DeclarationModifiers.Async;
                        break;

                    case SyntaxKind.ConstKeyword:
                        modifiers = modifiers | DeclarationModifiers.Const;
                        break;

                    case SyntaxKind.ReadOnlyKeyword:
                        modifiers = modifiers | DeclarationModifiers.ReadOnly;
                        break;

                    case SyntaxKind.SealedKeyword:
                        modifiers = modifiers | DeclarationModifiers.Sealed;
                        break;

                    case SyntaxKind.UnsafeKeyword:
                        modifiers = modifiers | DeclarationModifiers.Unsafe;
                        break;

                    case SyntaxKind.PartialKeyword:
                        modifiers = modifiers | DeclarationModifiers.Partial;
                        break;
                }
            }
        }

        private TypeParameterListSyntax AsTypeParameterList(IEnumerable<string> typeParameterNames)
        { 
            var typeParameters = typeParameterNames != null
                ? SyntaxFactory.TypeParameterList(SyntaxFactory.SeparatedList(typeParameterNames.Select(name => SyntaxFactory.TypeParameter(name))))
                : null;

            if (typeParameters != null && typeParameters.Parameters.Count == 0)
            {
                typeParameters = null;
            }

            return typeParameters;
        }

        public override SyntaxNode WithTypeParameters(SyntaxNode declaration, IEnumerable<string> typeParameterNames)
        {
            var typeParameters = this.AsTypeParameterList(typeParameterNames);

            switch (declaration.CSharpKind())
            {
                case SyntaxKind.MethodDeclaration:
                    return ((MethodDeclarationSyntax)declaration).WithTypeParameterList(typeParameters);

                case SyntaxKind.ClassDeclaration:
                    return ((ClassDeclarationSyntax)declaration).WithTypeParameterList(typeParameters);

                case SyntaxKind.StructDeclaration:
                    return ((StructDeclarationSyntax)declaration).WithTypeParameterList(typeParameters);

                case SyntaxKind.InterfaceDeclaration:
                    return ((InterfaceDeclarationSyntax)declaration).WithTypeParameterList(typeParameters);

                case SyntaxKind.DelegateDeclaration:
                    return ((DelegateDeclarationSyntax)declaration).WithTypeParameterList(typeParameters);
                default:
                    return declaration;
            }
        }

        public override SyntaxNode WithTypeConstraint(SyntaxNode declaration, string typeParameterName, SpecialTypeConstraintKind kinds, IEnumerable<SyntaxNode> types)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.MethodDeclaration:
                    var method = (MethodDeclarationSyntax)declaration;
                    return method.WithConstraintClauses(WithTypeConstraints(method.ConstraintClauses, typeParameterName, kinds, types));

                case SyntaxKind.ClassDeclaration:
                    var cls = (ClassDeclarationSyntax)declaration;
                    return cls.WithConstraintClauses(WithTypeConstraints(cls.ConstraintClauses, typeParameterName, kinds, types));

                case SyntaxKind.StructDeclaration:
                    var str = (StructDeclarationSyntax)declaration;
                    return str.WithConstraintClauses(WithTypeConstraints(str.ConstraintClauses, typeParameterName, kinds, types));

                case SyntaxKind.InterfaceDeclaration:
                    var iface = (InterfaceDeclarationSyntax)declaration;
                    return iface.WithConstraintClauses(WithTypeConstraints(iface.ConstraintClauses, typeParameterName, kinds, types));

                case SyntaxKind.DelegateDeclaration:
                    var del = (DelegateDeclarationSyntax)declaration;
                    return del.WithConstraintClauses(WithTypeConstraints(del.ConstraintClauses, typeParameterName, kinds, types));
                default:
                    return declaration;
            }
        }

        private SyntaxList<TypeParameterConstraintClauseSyntax> WithTypeConstraints(
            SyntaxList<TypeParameterConstraintClauseSyntax> clauses, string typeParameterName, SpecialTypeConstraintKind kinds, IEnumerable<SyntaxNode> types)
        {
            var constraints = types != null
                ? SyntaxFactory.SeparatedList<TypeParameterConstraintSyntax>(types.Select(t => SyntaxFactory.TypeConstraint((TypeSyntax)t)))
                : SyntaxFactory.SeparatedList<TypeParameterConstraintSyntax>();

            if ((kinds & SpecialTypeConstraintKind.Constructor) != 0)
            {
                constraints = constraints.Add(SyntaxFactory.ConstructorConstraint());
            }

            var isReferenceType = (kinds & SpecialTypeConstraintKind.ReferenceType) != 0;
            var isValueType = (kinds & SpecialTypeConstraintKind.ValueType) != 0;

            if (isReferenceType || isValueType)
            {
                constraints = constraints.Insert(0, SyntaxFactory.ClassOrStructConstraint(isReferenceType ? SyntaxKind.ClassConstraint : SyntaxKind.StructConstraint));
            }

            var clause = clauses.FirstOrDefault(c => c.Name.Identifier.ToString() == typeParameterName);

            if (clause == null)
            {
                if (constraints.Count > 0)
                {
                    return clauses.Add(SyntaxFactory.TypeParameterConstraintClause(typeParameterName.ToIdentifierName(), constraints));
                }
                else
                {
                    return clauses;
                }
            }
            else if (constraints.Count == 0)
            {
                return clauses.Remove(clause);
            }
            else
            {
                return clauses.Replace(clause, clause.WithConstraints(constraints));
            }
        }

        public override DeclarationKind GetDeclarationKind(SyntaxNode declaration)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.ClassDeclaration:
                    return DeclarationKind.Class;
                case SyntaxKind.StructDeclaration:
                    return DeclarationKind.Struct;
                case SyntaxKind.InterfaceDeclaration:
                    return DeclarationKind.Interface;
                case SyntaxKind.EnumDeclaration:
                    return DeclarationKind.Enum;
                case SyntaxKind.DelegateDeclaration:
                    return DeclarationKind.Delegate;

                case SyntaxKind.MethodDeclaration:
                    return DeclarationKind.Method;
                case SyntaxKind.OperatorDeclaration:
                    return DeclarationKind.Operator;
                case SyntaxKind.ConversionOperatorDeclaration:
                    return DeclarationKind.ConversionOperator;
                case SyntaxKind.ConstructorDeclaration:
                    return DeclarationKind.Constructor;
                case SyntaxKind.DestructorDeclaration:
                    return DeclarationKind.Destructor;

                case SyntaxKind.PropertyDeclaration:
                    return DeclarationKind.Property;
                case SyntaxKind.IndexerDeclaration:
                    return DeclarationKind.Indexer;
                case SyntaxKind.EventDeclaration:
                    return DeclarationKind.CustomEvent;
                case SyntaxKind.EnumMemberDeclaration:
                    return DeclarationKind.EnumMember;
                case SyntaxKind.CompilationUnit:
                    return DeclarationKind.CompilationUnit;
                case SyntaxKind.NamespaceDeclaration:
                    return DeclarationKind.Namespace;
                case SyntaxKind.UsingDirective:
                    return DeclarationKind.NamespaceImport;
                case SyntaxKind.Parameter:
                    return DeclarationKind.Parameter;

                case SyntaxKind.ParenthesizedLambdaExpression:
                case SyntaxKind.SimpleLambdaExpression:
                    return DeclarationKind.LambdaExpression;

                case SyntaxKind.FieldDeclaration:
                    var fd = (FieldDeclarationSyntax)declaration;
                    if (fd.Declaration != null && fd.Declaration.Variables.Count == 1)
                    {
                        // this node is considered the declaration if it contains only one variable.
                        return DeclarationKind.Field;
                    }
                    else
                    {
                        return DeclarationKind.None;
                    }

                case SyntaxKind.EventFieldDeclaration:
                    var ef = (EventFieldDeclarationSyntax)declaration;
                    if (ef.Declaration != null && ef.Declaration.Variables.Count == 1)
                    {
                        // this node is considered the declaration if it contains only one variable.
                        return DeclarationKind.Event;
                    }
                    else
                    {
                        return DeclarationKind.None;
                    }

                case SyntaxKind.LocalDeclarationStatement:
                    var ld = (LocalDeclarationStatementSyntax)declaration;
                    if (ld.Declaration != null && ld.Declaration.Variables.Count == 1)
                    {
                        // this node is considered the declaration if it contains only one variable.
                        return DeclarationKind.Variable;
                    }
                    else
                    {
                        return DeclarationKind.None;
                    }

                case SyntaxKind.VariableDeclaration:
                    {
                        var vd = (VariableDeclarationSyntax)declaration;
                        if (vd.Variables.Count == 1 && vd.Parent == null)
                        {
                            // this node is the declaration if it contains only one variable and has no parent.
                            return DeclarationKind.Variable;
                        }
                        else
                        {
                            return DeclarationKind.None; 
                        }
                    }

                case SyntaxKind.VariableDeclarator:
                    {
                        var vd = declaration.Parent as VariableDeclarationSyntax;

                        // this node is considered the declaration if it is one among many, or it has no parent
                        if (vd == null || vd.Variables.Count > 1)
                        {
                            if (ParentIsFieldDeclaration(vd))
                            {
                                return DeclarationKind.Field;
                            }
                            else if (ParentIsEventFieldDeclaration(vd))
                            {
                                return DeclarationKind.Event;
                            }
                            else
                            {
                                return DeclarationKind.Variable;
                            }
                        }
                        break;
                    }

                case SyntaxKind.AttributeList:
                    var list = (AttributeListSyntax)declaration;
                    if (list.Attributes.Count == 1)
                    {
                        return DeclarationKind.Attribute;
                    }
                    break;

                case SyntaxKind.Attribute:
                    var parentList = declaration.Parent as AttributeListSyntax;
                    if (parentList == null || parentList.Attributes.Count > 1)
                    {
                        return DeclarationKind.Attribute;
                    }
                    break;
            }

            return DeclarationKind.None;
        }

        private bool ParentIsFieldDeclaration(SyntaxNode node)
        {
            return node != null && node.Parent != null && node.Parent.IsKind(SyntaxKind.FieldDeclaration);
        }

        private bool ParentIsEventFieldDeclaration(SyntaxNode node)
        {
            return node != null && node.Parent != null && node.Parent.IsKind(SyntaxKind.EventFieldDeclaration);
        }

        private bool ParentIsLocalDeclarationStatement(SyntaxNode node)
        {
            return node != null && node.Parent != null && node.Parent.IsKind(SyntaxKind.LocalDeclarationStatement);
        }

        public override string GetName(SyntaxNode declaration)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.ClassDeclaration:
                    return ((ClassDeclarationSyntax)declaration).Identifier.ValueText;
                case SyntaxKind.StructDeclaration:
                    return ((StructDeclarationSyntax)declaration).Identifier.ValueText;
                case SyntaxKind.InterfaceDeclaration:
                    return ((InterfaceDeclarationSyntax)declaration).Identifier.ValueText;
                case SyntaxKind.EnumDeclaration:
                    return ((EnumDeclarationSyntax)declaration).Identifier.ValueText;
                case SyntaxKind.DelegateDeclaration:
                    return ((DelegateDeclarationSyntax)declaration).Identifier.ValueText;
                case SyntaxKind.MethodDeclaration:
                    return ((MethodDeclarationSyntax)declaration).Identifier.ValueText;
                case SyntaxKind.FieldDeclaration:
                    return GetName(((FieldDeclarationSyntax)declaration).Declaration);
                case SyntaxKind.PropertyDeclaration:
                    return ((PropertyDeclarationSyntax)declaration).Identifier.ValueText;
                case SyntaxKind.EnumMemberDeclaration:
                    return ((EnumMemberDeclarationSyntax)declaration).Identifier.ValueText;
                case SyntaxKind.EventFieldDeclaration:
                    return GetName(((EventFieldDeclarationSyntax)declaration).Declaration);
                case SyntaxKind.EventDeclaration:
                    return ((EventDeclarationSyntax)declaration).Identifier.ValueText;
                case SyntaxKind.NamespaceDeclaration:
                    return ((NamespaceDeclarationSyntax)declaration).Name.ToString();
                case SyntaxKind.UsingDirective:
                    return ((UsingDirectiveSyntax)declaration).Name.ToString();
                case SyntaxKind.Parameter:
                    return ((ParameterSyntax)declaration).Identifier.ValueText;
                case SyntaxKind.LocalDeclarationStatement:
                    return GetName(((LocalDeclarationStatementSyntax)declaration).Declaration);
                case SyntaxKind.VariableDeclaration:
                    var vd = ((VariableDeclarationSyntax)declaration);
                    if (vd.Variables.Count == 1)
                    {
                        return vd.Variables[0].Identifier.ValueText;
                    }
                    break;
                case SyntaxKind.VariableDeclarator:
                    return ((VariableDeclaratorSyntax)declaration).Identifier.ValueText;
                case SyntaxKind.TypeParameter:
                    return ((TypeParameterSyntax)declaration).Identifier.ValueText;
                case SyntaxKind.AttributeList:
                    return ((AttributeListSyntax)declaration).Attributes[0].Name.ToString();
                case SyntaxKind.Attribute:
                    return ((AttributeSyntax)declaration).Name.ToString();
            }

            return string.Empty;
        }

        public override SyntaxNode WithName(SyntaxNode declaration, string name)
        {
            return Isolate(declaration, d => WithNameInternal(d, name));
        }

        private SyntaxNode WithNameInternal(SyntaxNode declaration, string name)
        {
            var id = name.ToIdentifierToken();

            switch (declaration.CSharpKind())
            {
                case SyntaxKind.ClassDeclaration:
                    return ReplaceWithTrivia(declaration, ((ClassDeclarationSyntax)declaration).Identifier, id);
                case SyntaxKind.StructDeclaration:
                    return ReplaceWithTrivia(declaration, ((StructDeclarationSyntax)declaration).Identifier, id);
                case SyntaxKind.InterfaceDeclaration:
                    return ReplaceWithTrivia(declaration, ((InterfaceDeclarationSyntax)declaration).Identifier, id);
                case SyntaxKind.EnumDeclaration:
                    return ReplaceWithTrivia(declaration, ((EnumDeclarationSyntax)declaration).Identifier, id);
                case SyntaxKind.DelegateDeclaration:
                    return ReplaceWithTrivia(declaration, ((DelegateDeclarationSyntax)declaration).Identifier, id);
                case SyntaxKind.MethodDeclaration:
                    return ReplaceWithTrivia(declaration, ((MethodDeclarationSyntax)declaration).Identifier, id);
                case SyntaxKind.FieldDeclaration:
                    return ReplaceWithTrivia(declaration, ((FieldDeclarationSyntax)declaration).Declaration.Variables[0].Identifier, id);
                case SyntaxKind.PropertyDeclaration:
                    return ReplaceWithTrivia(declaration, ((PropertyDeclarationSyntax)declaration).Identifier, id);
                case SyntaxKind.EnumMemberDeclaration:
                    return ReplaceWithTrivia(declaration, ((EnumMemberDeclarationSyntax)declaration).Identifier, id);
                case SyntaxKind.EventFieldDeclaration:
                    return ReplaceWithTrivia(declaration, ((EventFieldDeclarationSyntax)declaration).Declaration.Variables[0].Identifier, id);
                case SyntaxKind.EventDeclaration:
                    return ReplaceWithTrivia(declaration, ((EventDeclarationSyntax)declaration).Identifier, id);
                case SyntaxKind.NamespaceDeclaration:
                    return ReplaceWithTrivia(declaration, ((NamespaceDeclarationSyntax)declaration).Name, this.DottedName(name));
                case SyntaxKind.UsingDirective:
                    return ReplaceWithTrivia(declaration, ((UsingDirectiveSyntax)declaration).Name, this.DottedName(name));
                case SyntaxKind.Parameter:
                    return ReplaceWithTrivia(declaration, ((ParameterSyntax)declaration).Identifier, id);
                case SyntaxKind.LocalDeclarationStatement:
                    return ReplaceWithTrivia(declaration, ((LocalDeclarationStatementSyntax)declaration).Declaration.Variables[0].Identifier, id);
                case SyntaxKind.TypeParameter:
                    return ReplaceWithTrivia(declaration, ((TypeParameterSyntax)declaration).Identifier, id);
                case SyntaxKind.AttributeList:
                    return ReplaceWithTrivia(declaration, ((AttributeListSyntax)declaration).Attributes[0].Name, this.DottedName(name));
                case SyntaxKind.Attribute:
                    return ReplaceWithTrivia(declaration, ((AttributeSyntax)declaration).Name, this.DottedName(name));
                case SyntaxKind.VariableDeclaration:
                    return ReplaceWithTrivia(declaration, ((VariableDeclarationSyntax)declaration).Variables[0].Identifier, id);
                case SyntaxKind.VariableDeclarator:
                    return ReplaceWithTrivia(declaration, ((VariableDeclaratorSyntax)declaration).Identifier, id);

                default:
                    return declaration;
            }
        }

        public override SyntaxNode GetType(SyntaxNode declaration)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.DelegateDeclaration:
                    return NotVoid(((DelegateDeclarationSyntax)declaration).ReturnType);
                case SyntaxKind.MethodDeclaration:
                    return NotVoid(((MethodDeclarationSyntax)declaration).ReturnType);
                case SyntaxKind.FieldDeclaration:
                    return ((FieldDeclarationSyntax)declaration).Declaration.Type;
                case SyntaxKind.PropertyDeclaration:
                    return ((PropertyDeclarationSyntax)declaration).Type;
                case SyntaxKind.IndexerDeclaration:
                    return ((IndexerDeclarationSyntax)declaration).Type;
                case SyntaxKind.EventFieldDeclaration:
                    return ((EventFieldDeclarationSyntax)declaration).Declaration.Type;
                case SyntaxKind.EventDeclaration:
                    return ((EventDeclarationSyntax)declaration).Type;
                case SyntaxKind.Parameter:
                    return ((ParameterSyntax)declaration).Type;
                case SyntaxKind.LocalDeclarationStatement:
                    return ((LocalDeclarationStatementSyntax)declaration).Declaration.Type;
                case SyntaxKind.VariableDeclaration:
                    return ((VariableDeclarationSyntax)declaration).Type;
                case SyntaxKind.VariableDeclarator:
                    if (declaration.Parent != null)
                    {
                        return GetType(declaration.Parent);
                    }
                    break;
            }

            return null;
        }

        private TypeSyntax NotVoid(TypeSyntax type)
        {
            var pd = type as PredefinedTypeSyntax;
            return pd != null && pd.Keyword.IsKind(SyntaxKind.VoidKeyword) ? null : type;
        }

        public override SyntaxNode WithType(SyntaxNode declaration, SyntaxNode type)
        {
            return Isolate(declaration, d => WithTypeInternal(d, type));
        }

        private SyntaxNode WithTypeInternal(SyntaxNode declaration, SyntaxNode type)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.DelegateDeclaration:
                    return ((DelegateDeclarationSyntax)declaration).WithReturnType((TypeSyntax)type);
                case SyntaxKind.MethodDeclaration:
                    return ((MethodDeclarationSyntax)declaration).WithReturnType((TypeSyntax)type);
                case SyntaxKind.FieldDeclaration:
                    return ((FieldDeclarationSyntax)declaration).WithDeclaration(((FieldDeclarationSyntax)declaration).Declaration.WithType((TypeSyntax)type));
                case SyntaxKind.PropertyDeclaration:
                    return ((PropertyDeclarationSyntax)declaration).WithType((TypeSyntax)type);
                case SyntaxKind.IndexerDeclaration:
                    return ((IndexerDeclarationSyntax)declaration).WithType((TypeSyntax)type);
                case SyntaxKind.EventFieldDeclaration:
                    return ((EventFieldDeclarationSyntax)declaration).WithDeclaration(((EventFieldDeclarationSyntax)declaration).Declaration.WithType((TypeSyntax)type));
                case SyntaxKind.EventDeclaration:
                    return ((EventDeclarationSyntax)declaration).WithType((TypeSyntax)type);
                case SyntaxKind.Parameter:
                    return ((ParameterSyntax)declaration).WithType((TypeSyntax)type);
                case SyntaxKind.LocalDeclarationStatement:
                    return ((LocalDeclarationStatementSyntax)declaration).WithDeclaration(((LocalDeclarationStatementSyntax)declaration).Declaration.WithType((TypeSyntax)type));
                case SyntaxKind.VariableDeclaration:
                    return ((VariableDeclarationSyntax)declaration).WithType((TypeSyntax)type);
            }

            return declaration;
        }

        private SyntaxNode Isolate(SyntaxNode declaration, Func<SyntaxNode, SyntaxNode> editor)
        {
            var isolated = GetIsolatedDeclaration(declaration);

            var result = PreserveTrivia(isolated, editor);

            if (result == isolated)
            {
                return declaration;
            }
            else
            {
                return result;
            }
        }

        private SyntaxNode GetIsolatedDeclaration(SyntaxNode declaration)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.VariableDeclaration:
                    var vd = (VariableDeclarationSyntax)declaration;
                    if (vd.Parent != null && vd.Variables.Count == 1)
                    {
                        return GetIsolatedDeclaration(vd.Parent);
                    }
                    break;

                case SyntaxKind.VariableDeclarator:
                    var v = (VariableDeclaratorSyntax)declaration;
                    if (v.Parent != null && v.Parent.Parent != null)
                    {
                        return this.ClearTrivia(this.WithVariable(v.Parent.Parent, v));
                    }
                    break;
            }

            return declaration;
        }

        private SyntaxNode WithVariable(SyntaxNode declaration, VariableDeclaratorSyntax variable)
        {
            var vd = this.GetVariableDeclaration(declaration);
            if (vd != null)
            {
                return this.WithVariableDeclaration(declaration, vd.WithVariables(SyntaxFactory.SingletonSeparatedList(variable)));
            }

            return declaration;
        }

        private VariableDeclarationSyntax GetVariableDeclaration(SyntaxNode declaration)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.FieldDeclaration:
                    return ((FieldDeclarationSyntax)declaration).Declaration;
                case SyntaxKind.EventFieldDeclaration:
                    return ((EventFieldDeclarationSyntax)declaration).Declaration;
                case SyntaxKind.LocalDeclarationStatement:
                    return ((LocalDeclarationStatementSyntax)declaration).Declaration;
                default:
                    return null;
            }
        }

        private SyntaxNode WithVariableDeclaration(SyntaxNode declaration, VariableDeclarationSyntax variables)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.FieldDeclaration:
                    return ((FieldDeclarationSyntax)declaration).WithDeclaration(variables);
                case SyntaxKind.EventFieldDeclaration:
                    return ((EventFieldDeclarationSyntax)declaration).WithDeclaration(variables);
                case SyntaxKind.LocalDeclarationStatement:
                    return ((LocalDeclarationStatementSyntax)declaration).WithDeclaration(variables);
                default:
                    return declaration;
            }
        }

        private SyntaxNode GetFullDeclaration(SyntaxNode declaration)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.VariableDeclaration:
                    var vd = (VariableDeclarationSyntax)declaration;
                    if (ParentIsFieldDeclaration(vd)
                        || ParentIsEventFieldDeclaration(vd)
                        || ParentIsLocalDeclarationStatement(vd))
                    {
                        return vd.Parent;
                    }
                    else
                    {
                        return vd;
                    }

                case SyntaxKind.VariableDeclarator:
                case SyntaxKind.Attribute:
                    if (declaration.Parent != null)
                    {
                        return GetFullDeclaration(declaration.Parent);
                    }
                    break;
            }

            return declaration;
        }

        public override SyntaxNode ReplaceDeclaration(SyntaxNode root, SyntaxNode declaration, SyntaxNode newDeclaration)
        {
            if (newDeclaration == null || declaration.IsKind(newDeclaration.CSharpKind()))
            {
                return base.ReplaceDeclaration(root, declaration, newDeclaration);
            }

            var newKind = newDeclaration.CSharpKind();
            var fullDecl = this.GetFullDeclaration(declaration);

            if (fullDecl.IsKind(newKind))
            {
                if (AreSimilarExceptForSubDeclarations(fullDecl, newDeclaration))
                {
                    return base.ReplaceDeclaration(root, declaration, this.GetSubDeclarations(newDeclaration)[0]);
                }
                else
                {
                    var index = IndexOf(this.GetSubDeclarations(fullDecl), declaration);
                    return this.ReplaceSubDeclarationWithFullDeclaration(root, fullDecl, index, newDeclaration);
                }
            }

            return base.ReplaceDeclaration(root, declaration, newDeclaration);
        }

        public override SyntaxNode InsertDeclarationsBefore(SyntaxNode root, SyntaxNode declaration, IEnumerable<SyntaxNode> newDeclarations)
        {
            var fullDecl = this.GetFullDeclaration(declaration);
            if (fullDecl == declaration || GetDeclarationCount(fullDecl) == 1)
            {
                return base.InsertDeclarationsBefore(root, fullDecl, newDeclarations);
            }

            var subDecls = this.GetSubDeclarations(fullDecl);
            var count = subDecls.Count;
            var index = IndexOf(subDecls, declaration);

            // insert new declaration between full declaration split into two
            if (index > 0)
            {
                var newNodes = new List<SyntaxNode>();
                newNodes.Add(this.WithSubDeclarationsRemoved(fullDecl, index, count - index).WithTrailingTrivia(SyntaxFactory.ElasticSpace));
                newNodes.AddRange(newDeclarations);
                newNodes.Add(this.WithSubDeclarationsRemoved(fullDecl, 0, index).WithLeadingTrivia(SyntaxFactory.ElasticSpace));
                return ReplaceRange(root, fullDecl, newNodes);
            }

            return base.InsertDeclarationsBefore(root, fullDecl, newDeclarations);
        }

        private bool AreSimilarExceptForSubDeclarations(SyntaxNode decl1, SyntaxNode decl2)
        {
            var kind = decl1.CSharpKind();

            if (decl2.IsKind(kind))
            {
                switch (kind)
                {
                    case SyntaxKind.FieldDeclaration:
                        var fd1 = (FieldDeclarationSyntax)decl1;
                        var fd2 = (FieldDeclarationSyntax)decl2;
                        return SyntaxFactory.AreEquivalent(fd1.Declaration.Type, fd2.Declaration.Type)
                            && SyntaxFactory.AreEquivalent(fd1.Modifiers, fd2.Modifiers)
                            && SyntaxFactory.AreEquivalent(fd1.AttributeLists, fd2.AttributeLists);

                    case SyntaxKind.EventFieldDeclaration:
                        var efd1 = (EventFieldDeclarationSyntax)decl1;
                        var efd2 = (EventFieldDeclarationSyntax)decl2;
                        return SyntaxFactory.AreEquivalent(efd1.Declaration.Type, efd2.Declaration.Type)
                            && SyntaxFactory.AreEquivalent(efd1.Modifiers, efd2.Modifiers)
                            && SyntaxFactory.AreEquivalent(efd1.AttributeLists, efd2.AttributeLists);

                    case SyntaxKind.LocalDeclarationStatement:
                        var ld1 = (LocalDeclarationStatementSyntax)decl1;
                        var ld2 = (LocalDeclarationStatementSyntax)decl2;
                        return SyntaxFactory.AreEquivalent(ld1.Declaration.Type, ld2.Declaration.Type)
                            && SyntaxFactory.AreEquivalent(ld1.Modifiers, ld2.Modifiers);

                    case SyntaxKind.AttributeList:
                        var al1 = (AttributeListSyntax)decl1;
                        var al2 = (AttributeListSyntax)decl2;
                        return SyntaxFactory.AreEquivalent(al1.Target, al2.Target);
                }
            }

            return false;
        }

        private SyntaxNode ReplaceSubDeclarationWithFullDeclaration(SyntaxNode root, SyntaxNode declaration, int index, SyntaxNode newDeclaration)
        {
            var newNodes = new List<SyntaxNode>();
            var count = GetDeclarationCount(declaration);

            if (index >= 0 && index < count)
            {
                if (index > 0)
                {
                    newNodes.Add(this.WithSubDeclarationsRemoved(declaration, index, count - index).WithTrailingTrivia(SyntaxFactory.ElasticSpace));
                }

                newNodes.Add(newDeclaration);

                if (index < count - 1)
                {
                    newNodes.Add(this.WithSubDeclarationsRemoved(declaration, 0, index + 1).WithLeadingTrivia(SyntaxFactory.ElasticSpace));
                }

                return ReplaceRange(root, declaration, newNodes);
            }
            else
            {
                return root;
            }
        }

        private SyntaxNode WithSubDeclarationsRemoved(SyntaxNode declaration, int index, int count)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.FieldDeclaration:
                    var fd = (FieldDeclarationSyntax)declaration;
                    return fd.WithDeclaration(fd.Declaration.WithVariables(RemoveRange(fd.Declaration.Variables, index, count)));
                case SyntaxKind.EventFieldDeclaration:
                    var efd = (FieldDeclarationSyntax)declaration;
                    return efd.WithDeclaration(efd.Declaration.WithVariables(RemoveRange(efd.Declaration.Variables, index, count)));
                case SyntaxKind.LocalDeclarationStatement:
                    var ld = (LocalDeclarationStatementSyntax)declaration;
                    return ld.WithDeclaration(ld.Declaration.WithVariables(RemoveRange(ld.Declaration.Variables, index, count)));
                case SyntaxKind.VariableDeclaration:
                    var vd = (VariableDeclarationSyntax)declaration;
                    return vd.WithVariables(RemoveRange(vd.Variables, index, count));
                case SyntaxKind.AttributeList:
                    var al = (AttributeListSyntax)declaration;
                    return al.WithAttributes(RemoveRange(al.Attributes, index, count));
                default:
                    return declaration;
            }
        }

        private IReadOnlyList<SyntaxNode> GetSubDeclarations(SyntaxNode declaration)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.FieldDeclaration:
                    return ((FieldDeclarationSyntax)declaration).Declaration.Variables;
                case SyntaxKind.EventFieldDeclaration:
                    return ((FieldDeclarationSyntax)declaration).Declaration.Variables;
                case SyntaxKind.LocalDeclarationStatement:
                    return ((LocalDeclarationStatementSyntax)declaration).Declaration.Variables;
                case SyntaxKind.VariableDeclaration:
                    return ((VariableDeclarationSyntax)declaration).Variables;
                case SyntaxKind.AttributeList:
                    return ((AttributeListSyntax)declaration).Attributes;
                default:
                    return SpecializedCollections.EmptyReadOnlyList<SyntaxNode>();
            }
        }

        public override SyntaxNode RemoveDeclaration(SyntaxNode root, SyntaxNode declaration)
        {
            return this.Isolate(root.TrackNodes(declaration), r => RemoveDeclarationInternal(r, r.GetCurrentNode(declaration)));
        }

        private SyntaxNode RemoveDeclarationInternal(SyntaxNode root, SyntaxNode declaration)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.Attribute:
                    var attr = (AttributeSyntax)declaration;
                    var attrList = attr.Parent as AttributeListSyntax;
                    if (attrList != null && attrList.Attributes.Count == 1)
                    {
                        // remove entire list if only one attribute
                        return this.RemoveDeclaration(root, attrList);
                    }
                    break;

                case SyntaxKind.VariableDeclarator:
                    var full = this.GetFullDeclaration(declaration);
                    if (full != declaration && GetDeclarationCount(full) == 1)
                    {
                        // remove full declaration if only declarator
                        return this.RemoveDeclaration(root, full);
                    }
                    break;

                case SyntaxKind.AttributeArgument:
                    if (declaration.Parent != null && ((AttributeArgumentListSyntax)declaration.Parent).Arguments.Count == 1)
                    {
                        // remove entire argument list if only argument
                        return this.RemoveDeclaration(root, declaration.Parent);
                    }
                    break;
            }

            return base.RemoveDeclaration(root, declaration);
        }

        public override IReadOnlyList<SyntaxNode> GetParameters(SyntaxNode declaration)
        {
            var list = GetParameterList(declaration);
            if (list != null)
            {
                return list.Parameters;
            }
            else
            {
                return SpecializedCollections.EmptyReadOnlyList<SyntaxNode>();
            }
        }

        public override SyntaxNode InsertParameters(SyntaxNode declaration, int index, IEnumerable<SyntaxNode> parameters)
        {
            var newParameters = AsParameterList(this.ClearTrivia(parameters));

            var currentList = this.GetParameterList(declaration);
            if (currentList == null)
            {
                currentList = (declaration.IsKind(SyntaxKind.IndexerDeclaration))
                    ? (BaseParameterListSyntax)SyntaxFactory.BracketedParameterList()
                    : (BaseParameterListSyntax)SyntaxFactory.ParameterList();
            }

            var newList = currentList.WithParameters(currentList.Parameters.InsertRange(index, newParameters.Parameters));
            return this.WithParameterList(declaration, newList);
        }

        private BaseParameterListSyntax GetParameterList(SyntaxNode declaration)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.DelegateDeclaration:
                    return ((DelegateDeclarationSyntax)declaration).ParameterList;
                case SyntaxKind.MethodDeclaration:
                    return ((MethodDeclarationSyntax)declaration).ParameterList;
                case SyntaxKind.OperatorDeclaration:
                    return ((OperatorDeclarationSyntax)declaration).ParameterList;
                case SyntaxKind.ConversionOperatorDeclaration:
                    return ((ConversionOperatorDeclarationSyntax)declaration).ParameterList;
                case SyntaxKind.ConstructorDeclaration:
                    return ((ConstructorDeclarationSyntax)declaration).ParameterList;
                case SyntaxKind.DestructorDeclaration:
                    return ((DestructorDeclarationSyntax)declaration).ParameterList;
                case SyntaxKind.IndexerDeclaration:
                    return ((IndexerDeclarationSyntax)declaration).ParameterList;
                case SyntaxKind.ParenthesizedLambdaExpression:
                    return ((ParenthesizedLambdaExpressionSyntax)declaration).ParameterList;
                case SyntaxKind.SimpleLambdaExpression:
                    return  SyntaxFactory.ParameterList(SyntaxFactory.SingletonSeparatedList(((SimpleLambdaExpressionSyntax)declaration).Parameter));
                default:
                    return null;
            }
        }

        private SyntaxNode WithParameterList(SyntaxNode declaration, BaseParameterListSyntax list)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.DelegateDeclaration:
                    return ((DelegateDeclarationSyntax)declaration).WithParameterList(list);
                case SyntaxKind.MethodDeclaration:
                    return ((MethodDeclarationSyntax)declaration).WithParameterList(list);
                case SyntaxKind.OperatorDeclaration:
                    return ((OperatorDeclarationSyntax)declaration).WithParameterList(list);
                case SyntaxKind.ConversionOperatorDeclaration:
                    return ((ConversionOperatorDeclarationSyntax)declaration).WithParameterList(list);
                case SyntaxKind.ConstructorDeclaration:
                    return ((ConstructorDeclarationSyntax)declaration).WithParameterList(list);
                case SyntaxKind.DestructorDeclaration:
                    return ((DestructorDeclarationSyntax)declaration).WithParameterList(list);
                case SyntaxKind.IndexerDeclaration:
                    return ((IndexerDeclarationSyntax)declaration).WithParameterList(list);
                case SyntaxKind.ParenthesizedLambdaExpression:
                    return ((ParenthesizedLambdaExpressionSyntax)declaration).WithParameterList((ParameterListSyntax)list);
                case SyntaxKind.SimpleLambdaExpression:
                    var lambda = (SimpleLambdaExpressionSyntax)declaration;
                    var roList = AsReadOnlyList(list.Parameters);
                    if (roList.Count == 1 && IsSimpleLambdaParameter(roList[0]))
                    {
                        return lambda.WithParameter((ParameterSyntax)roList[0]);
                    }
                    else
                    {
                        return SyntaxFactory.ParenthesizedLambdaExpression(AsParameterList(roList), lambda.Body)
                            .WithLeadingTrivia(lambda.GetLeadingTrivia())
                            .WithTrailingTrivia(lambda.GetTrailingTrivia());
                    }
                default:
                    return declaration;
            }
        }

        public override SyntaxNode GetExpression(SyntaxNode declaration)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.ParenthesizedLambdaExpression:
                    return ((ParenthesizedLambdaExpressionSyntax)declaration).Body as ExpressionSyntax;
                case SyntaxKind.SimpleLambdaExpression:
                    return ((SimpleLambdaExpressionSyntax)declaration).Body as ExpressionSyntax;
                default:
                    return GetEqualsValue(declaration)?.Value;
            }
        }

        public override SyntaxNode WithExpression(SyntaxNode declaration, SyntaxNode expression)
        {
            return Isolate(declaration, d => WithExpressionInternal(d, ClearTrivia(expression)));
        }

        private SyntaxNode WithExpressionInternal(SyntaxNode declaration, SyntaxNode expression)
        {
            var expr = (ExpressionSyntax)expression;

            switch (declaration.CSharpKind())
            {
                case SyntaxKind.ParenthesizedLambdaExpression:
                    return ((ParenthesizedLambdaExpressionSyntax)declaration).WithBody((CSharpSyntaxNode)expr ?? CreateBlock(null));

                case SyntaxKind.SimpleLambdaExpression:
                    return ((SimpleLambdaExpressionSyntax)declaration).WithBody((CSharpSyntaxNode)expr ?? CreateBlock(null));

                default:
                    var eq = GetEqualsValue(declaration);
                    if (eq != null)
                    {
                        if (expression == null)
                        {
                            return this.WithEqualsValue(declaration, null);
                        }
                        else
                        {
                            // use replace so we only change the value part.
                            return ReplaceWithTrivia(declaration, eq.Value, expr);
                        }
                    }
                    else if (expression != null)
                    {
                        return this.WithEqualsValue(declaration, SyntaxFactory.EqualsValueClause(expr));
                    }
                    else
                    {
                        return declaration;
                    }
            }
        }

        private EqualsValueClauseSyntax GetEqualsValue(SyntaxNode declaration)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.FieldDeclaration:
                    return ((FieldDeclarationSyntax)declaration).Declaration.Variables[0].Initializer;
                case SyntaxKind.LocalDeclarationStatement:
                    return ((LocalDeclarationStatementSyntax)declaration).Declaration.Variables[0].Initializer;
                case SyntaxKind.VariableDeclaration:
                    return ((VariableDeclarationSyntax)declaration).Variables[0].Initializer;
                case SyntaxKind.VariableDeclarator:
                    return ((VariableDeclaratorSyntax)declaration).Initializer;
                case SyntaxKind.Parameter:
                    return ((ParameterSyntax)declaration).Default;
                default:
                    return null;
            }
        }

        private SyntaxNode WithEqualsValue(SyntaxNode declaration, EqualsValueClauseSyntax eq)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.FieldDeclaration:
                    return ((FieldDeclarationSyntax)declaration).Declaration.Variables[0].WithInitializer(eq);
                case SyntaxKind.LocalDeclarationStatement:
                    return ((LocalDeclarationStatementSyntax)declaration).Declaration.Variables[0].WithInitializer(eq);
                case SyntaxKind.VariableDeclaration:
                    return ((VariableDeclarationSyntax)declaration).Variables[0].WithInitializer(eq);
                case SyntaxKind.VariableDeclarator:
                    return ((VariableDeclaratorSyntax)declaration).WithInitializer(eq);
                case SyntaxKind.Parameter:
                    return ((ParameterSyntax)declaration).WithDefault(eq);
                default:
                    return declaration;
            }
        }

        public override IReadOnlyList<SyntaxNode> GetStatements(SyntaxNode declaration)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.MethodDeclaration:
                    return ((MethodDeclarationSyntax)declaration).Body?.Statements;
                case SyntaxKind.OperatorDeclaration:
                    return ((OperatorDeclarationSyntax)declaration).Body?.Statements;
                case SyntaxKind.ConversionOperatorDeclaration:
                    return ((ConversionOperatorDeclarationSyntax)declaration).Body?.Statements;
                case SyntaxKind.ConstructorDeclaration:
                    return ((ConstructorDeclarationSyntax)declaration).Body?.Statements;
                case SyntaxKind.DestructorDeclaration:
                    return ((DestructorDeclarationSyntax)declaration).Body?.Statements;
                case SyntaxKind.ParenthesizedLambdaExpression:
                    return (((ParenthesizedLambdaExpressionSyntax)declaration).Body as BlockSyntax)?.Statements;
                case SyntaxKind.SimpleLambdaExpression:
                    return (((SimpleLambdaExpressionSyntax)declaration).Body as BlockSyntax)?.Statements;
                default:
                    return SpecializedCollections.EmptyReadOnlyList<SyntaxNode>();
            }
        }

        public override SyntaxNode WithStatements(SyntaxNode declaration, IEnumerable<SyntaxNode> statements)
        {
            var body = CreateBlock(statements);

            switch (declaration.CSharpKind())
            {
                case SyntaxKind.DelegateDeclaration:
                    return ((DelegateDeclarationSyntax)declaration).WithBody(body);
                case SyntaxKind.MethodDeclaration:
                    return ((MethodDeclarationSyntax)declaration).WithBody(body);
                case SyntaxKind.OperatorDeclaration:
                    return ((OperatorDeclarationSyntax)declaration).WithBody(body);
                case SyntaxKind.ConversionOperatorDeclaration:
                    return ((ConversionOperatorDeclarationSyntax)declaration).WithBody(body);
                case SyntaxKind.ConstructorDeclaration:
                    return ((ConstructorDeclarationSyntax)declaration).WithBody(body);
                case SyntaxKind.DestructorDeclaration:
                    return ((DestructorDeclarationSyntax)declaration).WithBody(body);
                case SyntaxKind.ParenthesizedLambdaExpression:
                    return ((ParenthesizedLambdaExpressionSyntax)declaration).WithBody(CreateBlock(statements));
                case SyntaxKind.SimpleLambdaExpression:
                    return ((SimpleLambdaExpressionSyntax)declaration).WithBody(CreateBlock(statements));
                default:
                    return declaration;
            }
        }

        public override IReadOnlyList<SyntaxNode> GetGetAccessorStatements(SyntaxNode declaration)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.PropertyDeclaration:
                    return ((PropertyDeclarationSyntax)declaration).AccessorList.Accessors.FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration))?.Body?.Statements;
                case SyntaxKind.IndexerDeclaration:
                    return ((IndexerDeclarationSyntax)declaration).AccessorList.Accessors.FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration))?.Body?.Statements;
                default:
                    return SpecializedCollections.EmptyReadOnlyList<SyntaxNode>();
            }
        }

        public override SyntaxNode WithGetAccessorStatements(SyntaxNode declaration, IEnumerable<SyntaxNode> statements)
        {
            var getAccessor = AccessorDeclaration(SyntaxKind.GetAccessorDeclaration, statements);
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.PropertyDeclaration:
                    return WithAccessorDeclaration(declaration, ((PropertyDeclarationSyntax)declaration).AccessorList, getAccessor);
                case SyntaxKind.IndexerDeclaration:
                    return WithAccessorDeclaration(declaration, ((IndexerDeclarationSyntax)declaration).AccessorList, getAccessor);
                default:
                    return declaration;
            }
        }

        public override IReadOnlyList<SyntaxNode> GetSetAccessorStatements(SyntaxNode declaration)
        {
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.PropertyDeclaration:
                    return ((PropertyDeclarationSyntax)declaration).AccessorList.Accessors.FirstOrDefault(a => a.IsKind(SyntaxKind.SetAccessorDeclaration))?.Body?.Statements;
                case SyntaxKind.IndexerDeclaration:
                    return ((IndexerDeclarationSyntax)declaration).AccessorList.Accessors.FirstOrDefault(a => a.IsKind(SyntaxKind.SetAccessorDeclaration))?.Body?.Statements;
                default:
                    return SpecializedCollections.EmptyReadOnlyList<SyntaxNode>();
            }
        }

        public override SyntaxNode WithSetAccessorStatements(SyntaxNode declaration, IEnumerable<SyntaxNode> statements)
        {
            var setAccessor = AccessorDeclaration(SyntaxKind.SetAccessorDeclaration, statements);
            switch (declaration.CSharpKind())
            {
                case SyntaxKind.PropertyDeclaration:
                    return WithAccessorDeclaration(declaration, ((PropertyDeclarationSyntax)declaration).AccessorList, setAccessor);
                case SyntaxKind.IndexerDeclaration:
                    return WithAccessorDeclaration(declaration, ((IndexerDeclarationSyntax)declaration).AccessorList, setAccessor);
                default:
                    return declaration;
            }
        }

        private SyntaxNode WithAccessorDeclaration(SyntaxNode declaration, AccessorListSyntax accessorList, AccessorDeclarationSyntax accessor)
        {
            var acc = accessorList.Accessors.FirstOrDefault(a => a.IsKind(accessor.CSharpKind()));
            if (acc != null)
            {
                return declaration.ReplaceNode(acc, accessor);
            }
            else
            {
                return declaration.ReplaceNode(accessorList, accessorList.AddAccessors(accessor));
            }
        }

        #region Statements and Expressions
        public override SyntaxNode ReturnStatement(SyntaxNode expressionOpt = null)
        {
            return SyntaxFactory.ReturnStatement((ExpressionSyntax)expressionOpt);
        }

        public override SyntaxNode ThrowStatement(SyntaxNode expressionOpt = null)
        {
            return SyntaxFactory.ThrowStatement((ExpressionSyntax)expressionOpt);
        }

        public override SyntaxNode IfStatement(SyntaxNode condition, IEnumerable<SyntaxNode> trueStatements, IEnumerable<SyntaxNode> falseStatements = null)
        {
            if (falseStatements == null)
            {
                return SyntaxFactory.IfStatement(
                    (ExpressionSyntax)condition,
                    CreateBlock(trueStatements));
            }
            else
            {
                var falseArray = AsReadOnlyList(falseStatements);

                // make else-if chain if false-statements contain only an if-statement
                return SyntaxFactory.IfStatement(
                    (ExpressionSyntax)condition,
                    CreateBlock(trueStatements),
                    SyntaxFactory.ElseClause(
                        falseArray.Count == 1 && falseArray[0] is IfStatementSyntax ? (StatementSyntax)falseArray[0] : CreateBlock(falseArray)));
            }
        }

        private BlockSyntax CreateBlock(IEnumerable<SyntaxNode> statements)
        {
            return SyntaxFactory.Block(AsStatementList(statements));
        }

        private SyntaxList<StatementSyntax> AsStatementList(IEnumerable<SyntaxNode> nodes)
        {
            if (nodes == null)
            {
                return default(SyntaxList<StatementSyntax>);
            }
            else
            {
                return SyntaxFactory.List(nodes.Select(AsStatement));
            }
        }

        private StatementSyntax AsStatement(SyntaxNode node)
        {
            var expression = node as ExpressionSyntax;
            if (expression != null)
            {
                return SyntaxFactory.ExpressionStatement(expression);
            }

            return (StatementSyntax)node;
        }

        public override SyntaxNode ExpressionStatement(SyntaxNode expression)
        {
            return SyntaxFactory.ExpressionStatement((ExpressionSyntax)expression);
        }

        public override SyntaxNode MemberAccessExpression(SyntaxNode expression, SyntaxNode simpleName)
        {
            return SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                ParenthesizeLeft((ExpressionSyntax)expression), 
                (SimpleNameSyntax)simpleName);
        }

        // parenthesize the left hand size of a member access, invocation or element access expression
        private ExpressionSyntax ParenthesizeLeft(ExpressionSyntax expression)
        {
            if (expression is TypeSyntax
                || expression.IsKind(SyntaxKind.ThisExpression)
                || expression.IsKind(SyntaxKind.BaseExpression)
                || expression.IsKind(SyntaxKind.ParenthesizedExpression)
                || expression.IsKind(SyntaxKind.SimpleMemberAccessExpression)
                || expression.IsKind(SyntaxKind.InvocationExpression)
                || expression.IsKind(SyntaxKind.ElementAccessExpression))
            {
                return expression;
            }
            else
            {
                return this.Parenthesize(expression);
            }
        }

        public override SyntaxNode ObjectCreationExpression(SyntaxNode type, IEnumerable<SyntaxNode> arguments)
        {
            return SyntaxFactory.ObjectCreationExpression((TypeSyntax)type, CreateArgumentList(arguments), null);
        }

        private ArgumentListSyntax CreateArgumentList(IEnumerable<SyntaxNode> arguments)
        {
            return SyntaxFactory.ArgumentList(CreateArguments(arguments));
        }

        private SeparatedSyntaxList<ArgumentSyntax> CreateArguments(IEnumerable<SyntaxNode> arguments)
        {
            return SyntaxFactory.SeparatedList(arguments.Select(AsArgument).Cast<ArgumentSyntax>());
        }

        private ArgumentSyntax AsArgument(SyntaxNode argOrExpression)
        {
            var arg = argOrExpression as ArgumentSyntax;
            if (arg != null)
            {
                return arg;
            }
            else
            {
                return SyntaxFactory.Argument((ExpressionSyntax)argOrExpression);
            }
        }

        public override SyntaxNode InvocationExpression(SyntaxNode expression, IEnumerable<SyntaxNode> arguments)
        {
            return SyntaxFactory.InvocationExpression(ParenthesizeLeft((ExpressionSyntax)expression), CreateArgumentList(arguments));
        }

        public override SyntaxNode ElementAccessExpression(SyntaxNode expression, IEnumerable<SyntaxNode> arguments)
        {
            return SyntaxFactory.ElementAccessExpression(ParenthesizeLeft((ExpressionSyntax)expression), SyntaxFactory.BracketedArgumentList(CreateArguments(arguments)));
        }

        public override SyntaxNode DefaultExpression(SyntaxNode type)
        {
            return SyntaxFactory.DefaultExpression((TypeSyntax)type);
        }

        public override SyntaxNode DefaultExpression(ITypeSymbol type)
        {
            // If it's just a reference type, then "null" is the default expression for it.  Note:
            // this counts for actual reference type, or a type parameter with a 'class' constraint.
            // Also, if it's a nullable type, then we can use "null".
            if (type.IsReferenceType ||
                type.IsPointerType() ||
                type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                return SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
            }

            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean:
                    return SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression);
                case SpecialType.System_SByte:
                case SpecialType.System_Byte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Decimal:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                    return SyntaxFactory.LiteralExpression(
                        SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal("0", 0));
            }

            // Default to a "default(<typename>)" expression.
            return SyntaxFactory.DefaultExpression(type.GenerateTypeSyntax());
        }

        private ExpressionSyntax Parenthesize(SyntaxNode expression)
        {
            return ((ExpressionSyntax)expression).Parenthesize();
        }

        public override SyntaxNode IsTypeExpression(SyntaxNode expression, SyntaxNode type)
        {
            return SyntaxFactory.BinaryExpression(SyntaxKind.IsExpression, Parenthesize(expression), (TypeSyntax)type);
        }

        public override SyntaxNode TryCastExpression(SyntaxNode expression, SyntaxNode type)
        {
            return SyntaxFactory.BinaryExpression(SyntaxKind.AsExpression, Parenthesize(expression), (TypeSyntax)type);
        }

        public override SyntaxNode CastExpression(SyntaxNode type, SyntaxNode expression)
        {
            return SyntaxFactory.CastExpression((TypeSyntax)type, Parenthesize(expression));
        }

        public override SyntaxNode ConvertExpression(SyntaxNode type, SyntaxNode expression)
        {
            return SyntaxFactory.CastExpression((TypeSyntax)type, Parenthesize(expression));
        }

        public override SyntaxNode AssignmentStatement(SyntaxNode left, SyntaxNode right)
        {
            return SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, (ExpressionSyntax)left, Parenthesize(right));
        }

        private SyntaxNode CreateBinaryExpression(SyntaxKind syntaxKind, SyntaxNode left, SyntaxNode right)
        {
            return SyntaxFactory.BinaryExpression(syntaxKind, Parenthesize(left), Parenthesize(right));
        }

        public override SyntaxNode ValueEqualsExpression(SyntaxNode left, SyntaxNode right)
        {
            return CreateBinaryExpression(SyntaxKind.EqualsExpression, left, right);
        }

        public override SyntaxNode ReferenceEqualsExpression(SyntaxNode left, SyntaxNode right)
        {
            return CreateBinaryExpression(SyntaxKind.EqualsExpression, left, right);
        }

        public override SyntaxNode ValueNotEqualsExpression(SyntaxNode left, SyntaxNode right)
        {
            return CreateBinaryExpression(SyntaxKind.NotEqualsExpression, left, right);
        }

        public override SyntaxNode ReferenceNotEqualsExpression(SyntaxNode left, SyntaxNode right)
        {
            return CreateBinaryExpression(SyntaxKind.NotEqualsExpression, left, right);
        }

        public override SyntaxNode LessThanExpression(SyntaxNode left, SyntaxNode right)
        {
            return CreateBinaryExpression(SyntaxKind.LessThanExpression, left, right);
        }

        public override SyntaxNode LessThanOrEqualExpression(SyntaxNode left, SyntaxNode right)
        {
            return CreateBinaryExpression(SyntaxKind.LessThanOrEqualExpression, left, right);
        }

        public override SyntaxNode GreaterThanExpression(SyntaxNode left, SyntaxNode right)
        {
            return CreateBinaryExpression(SyntaxKind.GreaterThanExpression, left, right);
        }

        public override SyntaxNode GreaterThanOrEqualExpression(SyntaxNode left, SyntaxNode right)
        {
            return CreateBinaryExpression(SyntaxKind.GreaterThanOrEqualExpression, left, right);
        }

        public override SyntaxNode NegateExpression(SyntaxNode expression)
        {
            return SyntaxFactory.PrefixUnaryExpression(SyntaxKind.UnaryMinusExpression, Parenthesize(expression));
        }

        public override SyntaxNode AddExpression(SyntaxNode left, SyntaxNode right)
        {
            return CreateBinaryExpression(SyntaxKind.AddExpression, left, right);
        }

        public override SyntaxNode SubtractExpression(SyntaxNode left, SyntaxNode right)
        {
            return CreateBinaryExpression(SyntaxKind.SubtractExpression, left, right);
        }

        public override SyntaxNode MultiplyExpression(SyntaxNode left, SyntaxNode right)
        {
            return CreateBinaryExpression(SyntaxKind.MultiplyExpression, left, right);
        }

        public override SyntaxNode DivideExpression(SyntaxNode left, SyntaxNode right)
        {
            return CreateBinaryExpression(SyntaxKind.DivideExpression, left, right);
        }

        public override SyntaxNode ModuloExpression(SyntaxNode left, SyntaxNode right)
        {
            return CreateBinaryExpression(SyntaxKind.ModuloExpression, left, right);
        }

        public override SyntaxNode BitwiseAndExpression(SyntaxNode left, SyntaxNode right)
        {
            return CreateBinaryExpression(SyntaxKind.BitwiseAndExpression, left, right);
        }

        public override SyntaxNode BitwiseOrExpression(SyntaxNode left, SyntaxNode right)
        {
            return CreateBinaryExpression(SyntaxKind.BitwiseOrExpression, left, right);
        }

        public override SyntaxNode BitwiseNotExpression(SyntaxNode operand)
        {
            return SyntaxFactory.PrefixUnaryExpression(SyntaxKind.BitwiseNotExpression, Parenthesize(operand));
        }

        public override SyntaxNode LogicalAndExpression(SyntaxNode left, SyntaxNode right)
        {
            return CreateBinaryExpression(SyntaxKind.LogicalAndExpression, left, right);
        }

        public override SyntaxNode LogicalOrExpression(SyntaxNode left, SyntaxNode right)
        {
            return CreateBinaryExpression(SyntaxKind.LogicalOrExpression, left, right);
        }

        public override SyntaxNode LogicalNotExpression(SyntaxNode expression)
        {
            return SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, Parenthesize(expression));
        }

        public override SyntaxNode ConditionalExpression(SyntaxNode condition, SyntaxNode whenTrue, SyntaxNode whenFalse)
        {
            return SyntaxFactory.ConditionalExpression(Parenthesize(condition), Parenthesize(whenTrue), Parenthesize(whenFalse));
        }

        public override SyntaxNode CoalesceExpression(SyntaxNode left, SyntaxNode right)
        {
            return CreateBinaryExpression(SyntaxKind.CoalesceExpression, left, right);
        }

        public override SyntaxNode ThisExpression()
        {
            return SyntaxFactory.ThisExpression();
        }

        public override SyntaxNode BaseExpression()
        {
            return SyntaxFactory.BaseExpression();
        }

        public override SyntaxNode LiteralExpression(object value)
        {
            return ExpressionGenerator.GenerateNonEnumValueExpression(null, value, canUseFieldReference: true);
        }

        public override SyntaxNode IdentifierName(string identifier)
        {
            return identifier.ToIdentifierName();
        }

        public override SyntaxNode GenericName(string identifier, IEnumerable<SyntaxNode> typeArguments)
        {
            return SyntaxFactory.GenericName(identifier.ToIdentifierToken(),
                SyntaxFactory.TypeArgumentList(SyntaxFactory.SeparatedList(typeArguments.Cast<TypeSyntax>())));
        }

        public override SyntaxNode WithTypeArguments(SyntaxNode expression, IEnumerable<SyntaxNode> typeArguments)
        {
            switch (expression.CSharpKind())
            {
                case SyntaxKind.IdentifierName:
                case SyntaxKind.GenericName:
                    var sname = (SimpleNameSyntax)expression;
                    return SyntaxFactory.GenericName(sname.Identifier, SyntaxFactory.TypeArgumentList(SyntaxFactory.SeparatedList(typeArguments.Cast<TypeSyntax>())));

                case SyntaxKind.QualifiedName:
                    var qname = (QualifiedNameSyntax)expression;
                    return SyntaxFactory.QualifiedName(qname.Left, (SimpleNameSyntax)WithTypeArguments(qname.Right, typeArguments));

                case SyntaxKind.AliasQualifiedName:
                    var aname = (AliasQualifiedNameSyntax)expression;
                    return SyntaxFactory.AliasQualifiedName(aname.Alias, (SimpleNameSyntax)WithTypeArguments(aname.Name, typeArguments));

                case SyntaxKind.SimpleMemberAccessExpression:
                case SyntaxKind.PointerMemberAccessExpression:
                    var sma = (MemberAccessExpressionSyntax)expression;
                    return SyntaxFactory.MemberAccessExpression(expression.CSharpKind(), sma.Expression, (SimpleNameSyntax)WithTypeArguments(sma.Name, typeArguments));

                default:
                    return expression;
            }
        }

        public override SyntaxNode QualifiedName(SyntaxNode left, SyntaxNode right)
        {
            return SyntaxFactory.QualifiedName((NameSyntax)left, (SimpleNameSyntax)right);
        }

        public override SyntaxNode TypeExpression(ITypeSymbol typeSymbol)
        {
            return typeSymbol.GenerateTypeSyntax();
        }

        public override SyntaxNode TypeExpression(SpecialType specialType)
        {
            switch (specialType)
            {
                case SpecialType.System_Boolean:
                    return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword));
                case SpecialType.System_Byte:
                    return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ByteKeyword));
                case SpecialType.System_Char:
                    return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.CharKeyword));
                case SpecialType.System_Decimal:
                    return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.DecimalKeyword));
                case SpecialType.System_Double:
                    return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.DoubleKeyword));
                case SpecialType.System_Int16:
                    return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ShortKeyword));
                case SpecialType.System_Int32:
                    return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword));
                case SpecialType.System_Int64:
                    return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.LongKeyword));
                case SpecialType.System_Object:
                    return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword));
                case SpecialType.System_SByte:
                    return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.SByteKeyword));
                case SpecialType.System_Single:
                    return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.FloatKeyword));
                case SpecialType.System_String:
                    return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword));
                case SpecialType.System_UInt16:
                    return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.UShortKeyword));
                case SpecialType.System_UInt32:
                    return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.UIntKeyword));
                case SpecialType.System_UInt64:
                    return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ULongKeyword));
                default:
                    throw new NotSupportedException("Unsupported SpecialType");
            }
        }

        public override SyntaxNode ArrayTypeExpression(SyntaxNode type)
        {
            return SyntaxFactory.ArrayType((TypeSyntax)type, SyntaxFactory.SingletonList(SyntaxFactory.ArrayRankSpecifier()));
        }

        public override SyntaxNode NullableTypeExpression(SyntaxNode type)
        {
            if (type is NullableTypeSyntax)
            {
                return type;
            }
            else
            {
                return SyntaxFactory.NullableType((TypeSyntax)type);
            }
        }

        public override SyntaxNode Argument(string nameOpt, RefKind refKind, SyntaxNode expression)
        {
            return SyntaxFactory.Argument(
                nameOpt == null ? null : SyntaxFactory.NameColon(nameOpt),
                refKind == RefKind.Ref ? SyntaxFactory.Token(SyntaxKind.RefKeyword) :
                refKind == RefKind.Out ? SyntaxFactory.Token(SyntaxKind.OutKeyword) : default(SyntaxToken),
                (ExpressionSyntax)expression);
        }

        public override SyntaxNode LocalDeclarationStatement(SyntaxNode type, string name, SyntaxNode initializer, bool isConst)
        {
            return SyntaxFactory.LocalDeclarationStatement(
                isConst ? SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.ConstKeyword)) : default(SyntaxTokenList),
                this.VariableDeclaration(type, name, initializer));
        }

        private VariableDeclarationSyntax VariableDeclaration(SyntaxNode type, string name, SyntaxNode expression = null)
        {
            return SyntaxFactory.VariableDeclaration(
                type == null ? SyntaxFactory.IdentifierName("var") : (TypeSyntax)type,
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.VariableDeclarator(
                        name.ToIdentifierToken(),
                        null,
                        expression == null ? null : SyntaxFactory.EqualsValueClause((ExpressionSyntax)expression))));
        }

        public override SyntaxNode UsingStatement(SyntaxNode type, string name, SyntaxNode expression, IEnumerable<SyntaxNode> statements)
        {
            return SyntaxFactory.UsingStatement(
                VariableDeclaration(type, name, expression),
                expression: null,
                statement: CreateBlock(statements));
        }

        public override SyntaxNode UsingStatement(SyntaxNode expression, IEnumerable<SyntaxNode> statements)
        {
            return SyntaxFactory.UsingStatement(
                declaration: null,
                expression: (ExpressionSyntax)expression,
                statement: CreateBlock(statements));
        }

        public override SyntaxNode TryCatchStatement(IEnumerable<SyntaxNode> tryStatements, IEnumerable<SyntaxNode> catchClauses, IEnumerable<SyntaxNode> finallyStatements = null)
        {
            return SyntaxFactory.TryStatement(
                CreateBlock(tryStatements),
                catchClauses != null ? SyntaxFactory.List(catchClauses.Cast<CatchClauseSyntax>()) : default(SyntaxList<CatchClauseSyntax>),
                finallyStatements != null ? SyntaxFactory.FinallyClause(CreateBlock(finallyStatements)) : null);
        }

        public override SyntaxNode CatchClause(SyntaxNode type, string name, IEnumerable<SyntaxNode> statements)
        {
            return SyntaxFactory.CatchClause(
                SyntaxFactory.CatchDeclaration((TypeSyntax)type, name.ToIdentifierToken()), 
                filter: null, 
                block: CreateBlock(statements));
        }

        public override SyntaxNode WhileStatement(SyntaxNode condition, IEnumerable<SyntaxNode> statements)
        {
            return SyntaxFactory.WhileStatement((ExpressionSyntax)condition, CreateBlock(statements));
        }

        public override SyntaxNode SwitchStatement(SyntaxNode expression, IEnumerable<SyntaxNode> caseClauses)
        {
            return SyntaxFactory.SwitchStatement(
                (ExpressionSyntax)expression,
                caseClauses.Cast<SwitchSectionSyntax>().ToSyntaxList());
        }

        public override SyntaxNode SwitchSection(IEnumerable<SyntaxNode> expressions, IEnumerable<SyntaxNode> statements)
        {
            return SyntaxFactory.SwitchSection(AsSwitchLabels(expressions), AsStatementList(statements));
        }

        public override SyntaxNode DefaultSwitchSection(IEnumerable<SyntaxNode> statements)
        {
            return SyntaxFactory.SwitchSection(SyntaxFactory.SingletonList(SyntaxFactory.DefaultSwitchLabel() as SwitchLabelSyntax), AsStatementList(statements));
        }

        private SyntaxList<SwitchLabelSyntax> AsSwitchLabels(IEnumerable<SyntaxNode> expressions)
        {
            var labels = default(SyntaxList<SwitchLabelSyntax>);

            if (expressions != null)
            {
                labels = labels.AddRange(expressions.Select(e => SyntaxFactory.CaseSwitchLabel((ExpressionSyntax)e)));
            }

            return labels;
        }

        public override SyntaxNode ExitSwitchStatement()
        {
            return SyntaxFactory.BreakStatement();
        }

        public override SyntaxNode ValueReturningLambdaExpression(IEnumerable<SyntaxNode> parameterDeclarations, SyntaxNode expression)
        {
            var prms = AsReadOnlyList(parameterDeclarations?.Cast<ParameterSyntax>());

            if (prms.Count == 1 && IsSimpleLambdaParameter(prms[0]))
            {
                return SyntaxFactory.SimpleLambdaExpression(prms[0], (CSharpSyntaxNode)expression);
            }
            else
            {
                return SyntaxFactory.ParenthesizedLambdaExpression(AsParameterList(prms), (CSharpSyntaxNode)expression);
            }
        }

        private bool IsSimpleLambdaParameter(SyntaxNode node)
        {
            var p = node as ParameterSyntax;
            return p != null && p.Type == null && p.Default == null && p.Modifiers.Count == 0;
        }

        public override SyntaxNode VoidReturningLambdaExpression(IEnumerable<SyntaxNode> lambdaParameters, SyntaxNode expression)
        {
            return ValueReturningLambdaExpression(lambdaParameters, expression);
        }

        public override SyntaxNode ValueReturningLambdaExpression(IEnumerable<SyntaxNode> parameterDeclarations, IEnumerable<SyntaxNode> statements)
        {
            return ValueReturningLambdaExpression(parameterDeclarations, CreateBlock(statements));
        }

        public override SyntaxNode VoidReturningLambdaExpression(IEnumerable<SyntaxNode> lambdaParameters, IEnumerable<SyntaxNode> statements)
        {
            return ValueReturningLambdaExpression(lambdaParameters, statements);
        }

        public override SyntaxNode LambdaParameter(string identifier, SyntaxNode type = null)
        {
            return ParameterDeclaration(identifier, type, null, RefKind.None);
        }

        private IReadOnlyList<T> AsReadOnlyList<T>(IEnumerable<T> sequence)
        {
            var list = sequence as IReadOnlyList<T>;

            if (list == null)
            {
                list = sequence.ToImmutableReadOnlyListOrEmpty();
            }

            return list;
        }
        #endregion
    }
}
