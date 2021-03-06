﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

namespace ZeroFormatter.CodeGenerator
{
    // Utility and Extension methods for Roslyn
    internal static class RoslynExtensions
    {
        public static async Task<Compilation> GetCompilationFromProject(string csprojPath, params string[] preprocessorSymbols)
        {
            // fucking workaround of resolve reference...
            var externalReferences = new List<PortableExecutableReference>();
            {
                var locations = new List<string>
                {
                    typeof(object).Assembly.Location, // mscorlib
                    typeof(System.Linq.Enumerable).Assembly.Location // core
                };

                var xElem = XElement.Load(csprojPath);
                var ns = xElem.Name.Namespace;

                var csProjRoot = Path.GetDirectoryName(csprojPath);
                var frameworkRoot = Path.GetDirectoryName(typeof(object).Assembly.Location);

                foreach (var item in xElem.Descendants(ns + "Reference"))
                {
                    var hintPath = item.Element(ns + "HintPath")?.Value;
                    if (hintPath == null)
                    {
                        var path = Path.Combine(frameworkRoot, item.Attribute("Include").Value + ".dll");
                        locations.Add(path);
                    }
                    else
                    {
                        locations.Add(Path.Combine(csProjRoot, hintPath));
                    }
                }

                foreach (var item in locations.Distinct())
                {
                    if (File.Exists(item))
                    {
                        externalReferences.Add(MetadataReference.CreateFromFile(item));
                    }
                }
            }

            RegisterVisualStudio();

            Console.WriteLine();
            Console.WriteLine($"Project Path: {csprojPath}");
            Console.WriteLine($"- {csprojPath = Path.GetFullPath(csprojPath)}");
            Console.WriteLine();

            var workspace = MSBuildWorkspace.Create();
            var project = await workspace.OpenProjectAsync(csprojPath).ConfigureAwait(false);
            project = project.AddMetadataReferences(externalReferences); // workaround:)
            project = project.WithParseOptions((project.ParseOptions as CSharpParseOptions).WithPreprocessorSymbols(preprocessorSymbols));

            var compilation = await project.GetCompilationAsync().ConfigureAwait(false);
            return compilation;
        }

        static void RegisterVisualStudio()
        {
            var vsInstance = GetVisualStudioInstances();
            MSBuildLocator.RegisterInstance(vsInstance);

            //MSBuildLocator.RegisterDefaults();
        }

        static VisualStudioInstance GetVisualStudioInstances()
        {
            var vsInstanceArray = MSBuildLocator.QueryVisualStudioInstances().ToArray();

            var index = 0;
            foreach (var vsInstance in vsInstanceArray)
            {
                Console.WriteLine();
                Console.WriteLine($"Instance [Index: {index}]");
                Console.WriteLine($"\tName: {vsInstance.Name}");
                Console.WriteLine($"\tVersion: {vsInstance.Version}");
                Console.WriteLine($"\tMSBuildPath: {vsInstance.MSBuildPath}");
                index++;
            }

            if (vsInstanceArray.Length == 1)
                return vsInstanceArray[0];

            Console.WriteLine("Select Index?:");
            var inputIndex = Console.ReadLine();
            var parseResult = int.TryParse(inputIndex, out index);

            return parseResult ? vsInstanceArray[index] : null;
        }

        public static IEnumerable<INamedTypeSymbol> GetNamedTypeSymbols(this Compilation compilation)
        {
            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                var semModel = compilation.GetSemanticModel(syntaxTree);

                foreach (var item in syntaxTree.GetRoot()
                    .DescendantNodes()
                    .Select(x => semModel.GetDeclaredSymbol(x))
                    .Where(x => x != null))
                {
                    var namedType = item as INamedTypeSymbol;
                    if (namedType != null)
                    {
                        yield return namedType;
                    }
                }
            }
        }

        public static IEnumerable<INamedTypeSymbol> EnumerateBaseType(this ITypeSymbol symbol)
        {
            var t = symbol.BaseType;
            while (t != null)
            {
                yield return t;
                t = t.BaseType;
            }
        }

        public static AttributeData FindAttribute(this IEnumerable<AttributeData> attributeDataList, string typeName)
        {
            return attributeDataList
                .Where(x => x.AttributeClass.ToDisplayString() == typeName)
                .FirstOrDefault();
        }

        public static AttributeData FindAttributeShortName(this IEnumerable<AttributeData> attributeDataList, string typeName)
        {
            return attributeDataList
                .Where(x => x.AttributeClass.Name == typeName)
                .FirstOrDefault();
        }

        public static AttributeData FindAttributeIncludeBasePropertyShortName(this IPropertySymbol property, string typeName)
        {
            do
            {
                var data = FindAttributeShortName(property.GetAttributes(), typeName);
                if (data != null) return data;
                property = property.OverriddenProperty;
            } while (property != null);

            return null;
        }

        public static AttributeSyntax FindAttribute(this BaseTypeDeclarationSyntax typeDeclaration, SemanticModel model, string typeName)
        {
            return typeDeclaration.AttributeLists
                .SelectMany(x => x.Attributes)
                .Where(x => ModelExtensions.GetTypeInfo(model, x).Type?.ToDisplayString() == typeName)
                .FirstOrDefault();
        }

        public static INamedTypeSymbol FindBaseTargetType(this ITypeSymbol symbol, string typeName)
        {
            return symbol.EnumerateBaseType()
                .Where(x => x.OriginalDefinition?.ToDisplayString() == typeName)
                .FirstOrDefault();
        }

        public static object GetSingleNamedArgumentValue(this AttributeData attribute, string key)
        {
            foreach (var item in attribute.NamedArguments)
            {
                if (item.Key == key)
                {
                    return item.Value.Value;
                }
            }

            return null;
        }

        public static bool IsNullable(this INamedTypeSymbol symbol)
        {
            if (symbol.IsGenericType)
            {
                if (symbol.ConstructUnboundGenericType().ToDisplayString() == "T?")
                {
                    return true;
                }
            }
            return false;
        }

        public static IEnumerable<ISymbol> GetAllMembers(this ITypeSymbol symbol)
        {
            var t = symbol;
            while (t != null)
            {
                foreach (var item in t.GetMembers())
                {
                    yield return item;
                }
                t = t.BaseType;
            }
        }
    }
}
