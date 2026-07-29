using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace WasmBridge.Sdk;

/// <summary>
/// Scans already-built assemblies for types annotated with
/// <c>WasmBridge.Attributes.WasmBridgeAttribute</c> and generates a TypeScript
/// <c>interface</c> describing the JS-visible shape of the matching generated bridge class
/// (see <c>GenerateWasmBridgesTask</c>) - one method signature per
/// <c>[WasmBridgeExport]</c> method, using the exact JS-visible name and types the bridge
/// exposes (return types that get auto-serialized to JSON by the bridge surface as
/// <c>string</c>, mirroring <c>GenerateWasmBridgesTask</c>'s own JSON-rootable detection).
/// </summary>
/// <remarks>
/// Written into the same output directory as <c>GenerateTypeScriptTypesTask</c>
/// (<c>$(WasmBridgeTypeScriptOutputPath)</c>) so it is picked up by the same front-end sync
/// step, alongside the per-type interfaces for <c>[WasmBridgeTsInterface]</c> roots.
/// </remarks>
public sealed class GenerateBridgeTypeScriptTask : Microsoft.Build.Utilities.Task
{
    private const string BridgeAttributesAssemblyName = "WasmBridge.Attributes";
    private const string BridgeAttributeFullName = "WasmBridge.Attributes.WasmBridgeAttribute";
    private const string BridgeExportAttributeFullName = "WasmBridge.Attributes.WasmBridgeExportAttribute";
    private const string TsInterfaceAttributeFullName = "WasmBridge.Attributes.WasmBridgeTsInterfaceAttribute";

    private static readonly Dictionary<string, string> TypeScriptAliases = new()
    {
        [typeof(void).FullName!] = "void",
        [typeof(string).FullName!] = "string",
        [typeof(bool).FullName!] = "boolean",
        [typeof(int).FullName!] = "number",
        [typeof(long).FullName!] = "number",
        [typeof(short).FullName!] = "number",
        [typeof(byte).FullName!] = "number",
        [typeof(sbyte).FullName!] = "number",
        [typeof(uint).FullName!] = "number",
        [typeof(ulong).FullName!] = "number",
        [typeof(ushort).FullName!] = "number",
        [typeof(double).FullName!] = "number",
        [typeof(float).FullName!] = "number",
        [typeof(decimal).FullName!] = "number",
        [typeof(char).FullName!] = "string",
    };

    /// <summary>The resolved reference assemblies of the consuming project (e.g. <c>@(ReferencePath)</c>).</summary>
    [Required]
    public ITaskItem[] Assemblies { get; set; } = Array.Empty<ITaskItem>();

    /// <summary>Directory the generated TypeScript files are written to.</summary>
    [Required]
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>The generated TypeScript files.</summary>
    [Output]
    public ITaskItem[] GeneratedFiles { get; set; } = Array.Empty<ITaskItem>();

    public override bool Execute()
    {
        List<string> assemblyPaths = Assemblies
            .Select(item => item.ItemSpec)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (assemblyPaths.Count == 0)
        {
            GeneratedFiles = Array.Empty<ITaskItem>();
            return true;
        }

        Directory.CreateDirectory(OutputPath);

        var generatedFiles = new List<string>();

        using var context = new MetadataLoadContext(new PathAssemblyResolver(assemblyPaths));

        foreach (string path in assemblyPaths)
        {
            Assembly assembly;
            try
            {
                assembly = context.LoadFromAssemblyPath(path);
            }
            catch (Exception ex)
            {
                Log.LogMessage(MessageImportance.Low, $"GenerateBridgeTypeScript: skipping '{path}' ({ex.Message})");
                continue;
            }

            if (!ReferencesWasmBridgeAttributes(assembly))
            {
                continue;
            }

            foreach (Type type in GetLoadableTypes(assembly))
            {
                CustomAttributeData? bridgeAttribute = type.GetCustomAttributesData()
                    .FirstOrDefault(a => a.AttributeType.FullName == BridgeAttributeFullName);
                if (bridgeAttribute is null)
                {
                    continue;
                }

                string? file = GenerateBridgeInterfaceFile(type, bridgeAttribute);
                if (file is not null)
                {
                    generatedFiles.Add(file);
                }
            }
        }

        GeneratedFiles = generatedFiles.Select(f => (ITaskItem)new TaskItem(f)).ToArray();
        return !Log.HasLoggedErrors;
    }

    private static bool ReferencesWasmBridgeAttributes(Assembly assembly)
    {
        try
        {
            return assembly.GetReferencedAssemblies().Any(a => a.Name == BridgeAttributesAssemblyName);
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }

    private string? GenerateBridgeInterfaceFile(Type type, CustomAttributeData bridgeAttribute)
    {
        string generatedClassName = GetNamedStringArgument(bridgeAttribute, "ClassName") ?? $"{type.Name}Bridge";

        var methods = new List<(MethodInfo Method, CustomAttributeData Attribute)>();
        foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (method.IsSpecialName)
            {
                continue;
            }

            CustomAttributeData? methodAttribute = method.GetCustomAttributesData()
                .FirstOrDefault(a => a.AttributeType.FullName == BridgeExportAttributeFullName);
            if (methodAttribute is null)
            {
                continue;
            }

            methods.Add((method, methodAttribute));
        }

        if (methods.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// Generated by WasmBridge.Tasks. Do not edit by hand.");
        sb.AppendLine();
        sb.AppendLine($"export interface {generatedClassName} {{");

        foreach ((MethodInfo method, CustomAttributeData attribute) in methods)
        {
            string exportedName = GetNamedStringArgument(attribute, "Name") ?? method.Name;
            string parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.Name}: {GetTypeScriptType(p.ParameterType)}"));
            string returnType = IsJsonSerialized(method.ReturnType) ? "string" : GetTypeScriptType(method.ReturnType);
            sb.AppendLine($"  {exportedName}: ({parameters}) => {returnType}");
        }

        sb.AppendLine("}");

        string fileName = ToCamelCase(generatedClassName);
        string filePath = Path.Combine(OutputPath, $"{fileName}.ts");
        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        return filePath;
    }

    private string GetTypeScriptType(Type type)
    {
        if (type.FullName is not null && TypeScriptAliases.TryGetValue(type.FullName, out string? alias))
        {
            return alias;
        }

        Log.LogError(
            $"GenerateBridgeTypeScript: unsupported type '{type.FullName}' used as a [WasmBridgeExport] " +
            "parameter or return type - only primitives, void, and [WasmBridgeTsInterface]-rooted types " +
            "(or a List<T> of one, for return types) are supported.");
        return "unknown";
    }

    /// <summary>
    /// Mirrors <c>GenerateWasmBridgesTask.GetJsonContextPropertyName</c>: <see langword="true"/> if
    /// <paramref name="type"/> is itself a <c>[WasmBridgeTsInterface]</c> root, or a closed
    /// <c>List&lt;T&gt;</c> of one, in which case the generated bridge method serializes it to a
    /// JSON string rather than returning it directly.
    /// </summary>
    private static bool IsJsonSerialized(Type type)
    {
        if (IsTsInterfaceRoot(type))
        {
            return true;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition().FullName == "System.Collections.Generic.List`1")
        {
            return IsTsInterfaceRoot(type.GetGenericArguments()[0]);
        }

        return false;
    }

    private static bool IsTsInterfaceRoot(Type type) =>
        type.GetCustomAttributesData().Any(a => a.AttributeType.FullName == TsInterfaceAttributeFullName);

    private static string? GetNamedStringArgument(CustomAttributeData attribute, string name)
    {
        foreach (CustomAttributeNamedArgument argument in attribute.NamedArguments)
        {
            if (argument.MemberName == name && argument.TypedValue.Value is string value)
            {
                return value;
            }
        }

        return null;
    }

    private static string ToCamelCase(string name) =>
        name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);
}
