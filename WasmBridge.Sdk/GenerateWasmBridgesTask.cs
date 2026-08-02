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
/// Scans a set of already-built assemblies for types annotated with
/// <c>WasmBridge.Attributes.WasmBridgeAttribute</c> and generates a source file per type,
/// exposing every method annotated with <c>WasmBridge.Attributes.WasmBridgeExportAttribute</c>
/// as a <c>[JSExport]</c> entry point on a generated static bridge class.
/// </summary>
/// <remarks>
/// Runs as an MSBuild task (rather than a Roslyn source generator) so the generated file is
/// an ordinary source file by the time the compiler's built-in JSExport interop generator
/// runs - that generator only looks at original source, not at source produced by other
/// generators.
/// </remarks>
public sealed class GenerateWasmBridgesTask : Microsoft.Build.Utilities.Task
{
    private const string BridgeAttributesAssemblyName = "WasmBridge.Attributes";
    private const string BridgeAttributeFullName = "WasmBridge.Attributes.WasmBridgeAttribute";
    private const string BridgeExportAttributeFullName = "WasmBridge.Attributes.WasmBridgeExportAttribute";
    private const string TsInterfaceAttributeFullName = "WasmBridge.Attributes.WasmBridgeTsInterfaceAttribute";

    private static readonly Dictionary<string, string> PrimitiveAliases = new()
    {
        [typeof(void).FullName!] = "void",
        [typeof(string).FullName!] = "string",
        [typeof(bool).FullName!] = "bool",
        [typeof(int).FullName!] = "int",
        [typeof(long).FullName!] = "long",
        [typeof(short).FullName!] = "short",
        [typeof(byte).FullName!] = "byte",
        [typeof(sbyte).FullName!] = "sbyte",
        [typeof(uint).FullName!] = "uint",
        [typeof(ulong).FullName!] = "ulong",
        [typeof(ushort).FullName!] = "ushort",
        [typeof(double).FullName!] = "double",
        [typeof(float).FullName!] = "float",
        [typeof(decimal).FullName!] = "decimal",
        [typeof(object).FullName!] = "object",
        [typeof(char).FullName!] = "char",
    };

    /// <summary>The resolved reference assemblies of the consuming project (e.g. <c>@(ReferencePath)</c>).</summary>
    [Required]
    public ITaskItem[] Assemblies { get; set; } = Array.Empty<ITaskItem>();

    /// <summary>Directory the generated source files are written to.</summary>
    [Required]
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>The generated source files, suitable for adding to <c>@(Compile)</c>.</summary>
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
                Log.LogMessage(MessageImportance.Low, $"WasmBridge: skipping '{path}' ({ex.Message})");
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

                string? file = GenerateBridgeFile(type, bridgeAttribute);
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

    private string? GenerateBridgeFile(Type type, CustomAttributeData bridgeAttribute)
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
            Log.LogWarning($"WasmBridge: '{type.FullName}' is annotated with [WasmBridge] but has no [WasmBridgeExport] methods.");
            return null;
        }

        string targetTypeName = "global::" + type.FullName;
        bool needsInstance = methods.Any(m => !m.Method.IsStatic);

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// Generated by WasmBridge.Sdk. Do not edit by hand.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System.Runtime.InteropServices.JavaScript;");
        sb.AppendLine();
        sb.AppendLine($"internal static partial class {generatedClassName}");
        sb.AppendLine("{");

        if (needsInstance)
        {
            sb.AppendLine($"    private static readonly {targetTypeName} _target = new {targetTypeName}();");
            sb.AppendLine();
        }

        foreach ((MethodInfo method, CustomAttributeData attribute) in methods)
        {
            string exportedName = GetNamedStringArgument(attribute, "Name") ?? method.Name;
            string parameters = string.Join(", ", method.GetParameters().Select(p => $"{GetTypeName(p.ParameterType)} {p.Name}"));
            string arguments = string.Join(", ", method.GetParameters().Select(p => p.Name));
            string invocationTarget = method.IsStatic ? targetTypeName : "_target";
            string call = $"{invocationTarget}.{method.Name}({arguments})";

            sb.AppendLine("    [JSExport]");

            bool isTask = TryUnwrapTask(method.ReturnType, out Type innerReturnType, out bool isVoidTask);
            if (isTask && isVoidTask)
            {
                // A plain (non-generic) Task with no return value - JSExport supports
                // Task directly (as a JS Promise<void>), just await it through.
                sb.AppendLine($"    internal static async global::System.Threading.Tasks.Task {exportedName}({parameters}) => await {call};");
            }
            else if (isTask)
            {
                string? taskJsonPropertyName = GetJsonContextPropertyName(innerReturnType);
                if (taskJsonPropertyName is not null)
                {
                    // The awaited result isn't JSExport-marshalable directly (it's a
                    // [WasmBridgeTsInterface]-rooted type or a List<T> of one), so await it
                    // and serialize the result to JSON, exposing a Task<string> instead.
                    sb.AppendLine($"    internal static async global::System.Threading.Tasks.Task<string> {exportedName}({parameters})");
                    sb.AppendLine("    {");
                    sb.AppendLine($"        var result = await {call};");
                    sb.AppendLine($"        return global::System.Text.Json.JsonSerializer.Serialize(result, WasmBridgeJsonContext.Default.{taskJsonPropertyName});");
                    sb.AppendLine("    }");
                }
                else
                {
                    // The awaited result is directly JSExport-marshalable (e.g. a primitive) -
                    // Task<T> of it is supported directly, pass the task through as-is.
                    string innerTypeName = GetTypeName(innerReturnType);
                    sb.AppendLine($"    internal static global::System.Threading.Tasks.Task<{innerTypeName}> {exportedName}({parameters}) => {call};");
                }
            }
            else
            {
                string? jsonPropertyName = GetJsonContextPropertyName(method.ReturnType);
                if (jsonPropertyName is not null)
                {
                    // The return type isn't JSExport-marshalable directly (it's a
                    // [WasmBridgeTsInterface]-rooted type or a List<T> of one), so serialize it
                    // to JSON using the SDK-generated WasmBridgeJsonContext instead.
                    sb.AppendLine($"    internal static string {exportedName}({parameters}) => global::System.Text.Json.JsonSerializer.Serialize({call}, WasmBridgeJsonContext.Default.{jsonPropertyName});");
                }
                else
                {
                    string returnType = GetTypeName(method.ReturnType);
                    sb.AppendLine($"    internal static {returnType} {exportedName}({parameters}) => {call};");
                }
            }
            sb.AppendLine();
        }

        sb.AppendLine("}");

        string filePath = Path.Combine(OutputPath, $"{generatedClassName}.g.cs");
        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        return filePath;
    }

    private static string GetTypeName(Type type)
    {
        if (type.FullName is not null && PrimitiveAliases.TryGetValue(type.FullName, out string? alias))
        {
            return alias;
        }

        return "global::" + type.FullName;
    }

    /// <summary>
    /// If <paramref name="type"/> is a <c>[WasmBridgeTsInterface]</c>-rooted type, or a closed
    /// <c>List&lt;T&gt;</c> of one, returns the matching property name on the SDK-generated
    /// <c>WasmBridgeJsonContext</c> (see <c>GenerateJsonSerializerContextTask</c>) to serialize
    /// it with. Otherwise returns <see langword="null"/> and the return type is passed through
    /// as-is (the existing primitive/verbatim behavior).
    /// </summary>
    private static string? GetJsonContextPropertyName(Type type)
    {
        if (IsTsInterfaceRoot(type))
        {
            return type.Name;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition().FullName == "System.Collections.Generic.List`1")
        {
            Type elementType = type.GetGenericArguments()[0];
            if (IsTsInterfaceRoot(elementType))
            {
                return "List" + elementType.Name;
            }
        }

        return null;
    }

    private static bool IsTsInterfaceRoot(Type type) =>
        type.GetCustomAttributesData().Any(a => a.AttributeType.FullName == TsInterfaceAttributeFullName);

    /// <summary>
    /// If <paramref name="type"/> is <c>System.Threading.Tasks.Task</c> or a closed
    /// <c>Task&lt;T&gt;</c>, returns <see langword="true"/> with <paramref name="innerType"/>
    /// set to <c>T</c> (or <see cref="void"/> when it's the non-generic <c>Task</c>, via
    /// <paramref name="isVoidTask"/>). Otherwise returns <see langword="false"/> and
    /// <paramref name="innerType"/> is <paramref name="type"/> unchanged.
    /// </summary>
    private static bool TryUnwrapTask(Type type, out Type innerType, out bool isVoidTask)
    {
        if (type.FullName == "System.Threading.Tasks.Task")
        {
            innerType = typeof(void);
            isVoidTask = true;
            return true;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition().FullName == "System.Threading.Tasks.Task`1")
        {
            innerType = type.GetGenericArguments()[0];
            isVoidTask = false;
            return true;
        }

        innerType = type;
        isVoidTask = false;
        return false;
    }

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
}
