﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Microsoft.Interop.UnitTests
{
    /// <summary>
    /// The target framework to compile against.
    /// </summary>
    public enum TestTargetFramework
    {
        /// <summary>
        /// The latest supported .NET Framework version.
        /// </summary>
        Framework,
        /// <summary>
        /// .NET standard 2.0
        /// </summary>
        Standard2_0,
        /// <summary>
        /// .NET Standard 2.1
        /// </summary>
        Standard2_1
    }

    public static class TestUtils
    {
        public static string ID(
            [CallerLineNumber] int lineNumber = 0,
            [CallerFilePath] string? filePath = null)
            => TestUtils.GetFileLineName(lineNumber, filePath);
        internal static string GetFileLineName(
            [CallerLineNumber] int lineNumber = 0,
            [CallerFilePath] string? filePath = null)
            => $"{Path.GetFileName(filePath)}:{lineNumber}";

        internal static void Use<T>(T _)
        {
            // Workaround for - xUnit1026 // Theory methods should use all of their parameters
        }

        /// <summary>
        /// Disable binding redirect warnings. They are disabled by default by the .NET SDK, but not by Roslyn.
        /// See https://github.com/dotnet/roslyn/issues/19640.
        /// </summary>
        internal static ImmutableDictionary<string, ReportDiagnostic> BindingRedirectWarnings { get; } = new Dictionary<string, ReportDiagnostic>()
            {
                { "CS1701", ReportDiagnostic.Suppress },
                { "CS1702", ReportDiagnostic.Suppress },
            }.ToImmutableDictionary();

        /// <summary>
        /// Assert the pre-source generator compilation has only
        /// the expected failure diagnostics.
        /// </summary>
        /// <param name="comp"></param>
        public static void AssertPreSourceGeneratorCompilation(Compilation comp, params string[] additionalAllowedDiagnostics)
        {
            var allowedDiagnostics = new HashSet<string>()
            {
                "CS8795", // Partial method impl missing
                "CS0234", // Missing type or namespace - LibraryImportAttribute
                "CS0246", // Missing type or namespace - LibraryImportAttribute
                "CS8019", // Unnecessary using
            };

            foreach (string diagnostic in additionalAllowedDiagnostics)
            {
                allowedDiagnostics.Add(diagnostic);
            }

            var compDiags = comp.GetDiagnostics();
            Assert.All(compDiags, diag =>
            {
                Assert.Subset(allowedDiagnostics, new HashSet<string> { diag.Id });
            });
        }

        /// <summary>
        /// Assert the post-source generator compilation has only
        /// the expected failure diagnostics.
        /// </summary>
        /// <param name="comp"></param>
        public static void AssertPostSourceGeneratorCompilation(Compilation comp, params string[] additionalAllowedDiagnostics)
        {
            var allowedDiagnostics = new HashSet<string>()
            {
                "CS8019", // Unnecessary using
            };

            foreach (string diagnostic in additionalAllowedDiagnostics)
            {
                allowedDiagnostics.Add(diagnostic);
            }

            var compDiags = comp.GetDiagnostics();
            Assert.All(compDiags, diag =>
            {
                Assert.Subset(allowedDiagnostics, new HashSet<string> { diag.Id });
            });
        }

        /// <summary>
        /// Create a compilation given source
        /// </summary>
        /// <param name="source">Source to compile</param>
        /// <param name="targetFramework">Target framework of the compilation</param>
        /// <param name="outputKind">Output type</param>
        /// <param name="refs">Additional metadata references</param>
        /// <param name="preprocessorSymbols">Prepocessor symbols</param>
        /// <param name="allowUnsafe">Indicate if the compilation should allow unsafe code blocks</param>
        /// <returns>The resulting compilation</returns>
        public static Compilation CreateCompilation(string source, OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary, IEnumerable<MetadataReference>? refs = null, IEnumerable<string>? preprocessorSymbols = null, bool allowUnsafe = true)
        {
            return CreateCompilation([source], outputKind, refs, preprocessorSymbols, allowUnsafe);
        }

        /// <summary>
        /// Create a compilation given sources
        /// </summary>
        /// <param name="sources">Sources to compile</param>
        /// <param name="targetFramework">Target framework of the compilation</param>
        /// <param name="outputKind">Output type</param>
        /// <param name="refs">Additional metadata references</param>
        /// <param name="preprocessorSymbols">Prepocessor symbols</param>
        /// <param name="allowUnsafe">Indicate if the compilation should allow unsafe code blocks</param>
        /// <returns>The resulting compilation</returns>
        public static Compilation CreateCompilation(string[] sources, OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary, IEnumerable<MetadataReference>? refs = null, IEnumerable<string>? preprocessorSymbols = null, bool allowUnsafe = true)
        {
            return CreateCompilation(
                sources.Select(source =>
                    CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview, preprocessorSymbols: preprocessorSymbols))).ToArray(),
                outputKind,
                refs,
                allowUnsafe);
        }

        /// <summary>
        /// Create a compilation given sources
        /// </summary>
        /// <param name="sources">Sources to compile</param>
        /// <param name="targetFramework">Target framework of the compilation</param>
        /// <param name="outputKind">Output type</param>
        /// <param name="refs">Additional metadata references</param>
        /// <param name="allowUnsafe">Indicate if the compilation should allow unsafe code blocks</param>
        /// <returns>The resulting compilation</returns>
        public static Compilation CreateCompilation(SyntaxTree[] sources, OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary, IEnumerable<MetadataReference>? refs = null, bool allowUnsafe = true)
        {
            ImmutableArray<MetadataReference> referenceAssemblies = [ ..SourceGenerators.Tests.LiveReferencePack.GetMetadataReferences(), GetAncillaryReference() ];

            if (refs is not null)
            {
                referenceAssemblies = referenceAssemblies.AddRange(refs);
            }

            return CSharpCompilation.Create("compilation",
                sources,
                referenceAssemblies,
                new CSharpCompilationOptions(outputKind, allowUnsafe: allowUnsafe, specificDiagnosticOptions: BindingRedirectWarnings));
        }

        /// <summary>
        /// Get the metadata reference for the ancillary interop helper assembly.
        /// </summary>
        /// <returns></returns>
        internal static MetadataReference GetAncillaryReference()
        {
            // Include the assembly containing the new types we are considering exposing publicly
            // but haven't put through API review.
            var attrAssem = typeof(MarshalEx).GetTypeInfo().Assembly;
            return MetadataReference.CreateFromFile(attrAssem.Location);
        }

        /// <summary>
        /// Run the supplied generators on the compilation.
        /// </summary>
        /// <param name="comp">Compilation target</param>
        /// <param name="diagnostics">Resulting diagnostics</param>
        /// <param name="generators">Source generator instances</param>
        /// <returns>The resulting compilation</returns>
        public static Compilation RunGenerators(Compilation comp, out ImmutableArray<Diagnostic> diagnostics, params IIncrementalGenerator[] generators)
        {
            CreateDriver(comp, null, generators).RunGeneratorsAndUpdateCompilation(comp, out var d, out diagnostics);
            return d;
        }

        /// <summary>
        /// Run the supplied generators on the compilation.
        /// </summary>
        /// <param name="comp">Compilation target</param>
        /// <param name="diagnostics">Resulting diagnostics</param>
        /// <param name="generators">Source generator instances</param>
        /// <returns>The resulting compilation</returns>
        public static Compilation RunGenerators(Compilation comp, AnalyzerConfigOptionsProvider options, out ImmutableArray<Diagnostic> diagnostics, params IIncrementalGenerator[] generators)
        {
            CreateDriver(comp, options, generators).RunGeneratorsAndUpdateCompilation(comp, out var d, out diagnostics);
            return d;
        }

        public static GeneratorDriver CreateDriver(Compilation c, AnalyzerConfigOptionsProvider? options, IIncrementalGenerator[] generators, GeneratorDriverOptions driverOptions = default)
            => CSharpGeneratorDriver.Create(
                ImmutableArray.Create(generators.Select(gen => gen.AsSourceGenerator()).ToArray()),
                parseOptions: (CSharpParseOptions)c.SyntaxTrees.First().Options,
                optionsProvider: options,
                driverOptions: driverOptions);
    }
}
