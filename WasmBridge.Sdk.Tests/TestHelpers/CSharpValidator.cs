using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace WasmBridge.Sdk.Tests.TestHelpers;

/// <summary>Parses generated C# source with Roslyn to confirm it's at least syntactically well-formed.</summary>
public static class CSharpValidator
{
    public static void AssertParsesWithoutSyntaxErrors(string source)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source);

        var errors = tree.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0, "Generated C# has syntax errors:\n" + string.Join("\n", errors) + "\n\nSource:\n" + source);
    }
}
