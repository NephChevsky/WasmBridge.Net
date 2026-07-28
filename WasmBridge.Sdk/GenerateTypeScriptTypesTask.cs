using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace WasmBridge.Sdk;

/// <summary>
/// Scans already-built assemblies for types annotated with
/// <c>WasmBridge.Attributes.WasmBridgeTsInterfaceAttribute</c>, and generates a matching
/// TypeScript file per root: one <c>interface</c> (or <c>type</c> alias, for dictionary-like
/// types) per reachable type, plus a <c>parseX</c> function that parses and deep-freezes a
/// JSON payload as that root type.
/// </summary>
/// <remarks>
/// Only recurses into types declared in the same assembly as the root type - everything else
/// (primitives, BCL collection types) is mapped to a plain TypeScript type inline. The
/// generated TypeScript name always matches the C# type name.
/// </remarks>
public sealed class GenerateTypeScriptTypesTask : Microsoft.Build.Utilities.Task
{
    private const string TsInterfaceAttributeFullName = "WasmBridge.Attributes.WasmBridgeTsInterfaceAttribute";
    private const string BridgeAttributesAssemblyName = "WasmBridge.Attributes";
    private const string NullableAttributeFullName = "System.Runtime.CompilerServices.NullableAttribute";
    private const string NullableContextAttributeFullName = "System.Runtime.CompilerServices.NullableContextAttribute";

    private static readonly Dictionary<string, string> NumericAliases = new()
    {
        ["System.Int32"] = "number",
        ["System.Int64"] = "number",
        ["System.Int16"] = "number",
        ["System.Byte"] = "number",
        ["System.SByte"] = "number",
        ["System.UInt32"] = "number",
        ["System.UInt64"] = "number",
        ["System.UInt16"] = "number",
        ["System.Double"] = "number",
        ["System.Single"] = "number",
        ["System.Decimal"] = "number",
    };

    private static readonly HashSet<string> ListLikeGenericTypes = new()
    {
        "System.Collections.Generic.List`1",
        "System.Collections.Generic.IList`1",
        "System.Collections.Generic.IReadOnlyList`1",
        "System.Collections.Generic.ICollection`1",
        "System.Collections.Generic.IReadOnlyCollection`1",
        "System.Collections.Generic.IEnumerable`1",
    };

    private static readonly HashSet<string> DictionaryLikeGenericTypes = new()
    {
        "System.Collections.Generic.Dictionary`2",
        "System.Collections.Generic.IDictionary`2",
        "System.Collections.Generic.IReadOnlyDictionary`2",
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

    private Assembly _rootAssembly = null!;
    private HashSet<Type> _visited = null!;
    private List<string> _blocks = null!;

    public override bool Execute()
    {
        List<string> assemblyPaths = Assemblies
            .Select(item => item.ItemSpec)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var generatedFiles = new List<string>();

        Directory.CreateDirectory(OutputPath);

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
                Log.LogMessage(MessageImportance.Low, $"GenerateTypeScriptTypes: skipping '{path}' ({ex.Message})");
                continue;
            }

            if (!ReferencesWasmBridgeAttributes(assembly))
            {
                continue;
            }

            foreach (Type type in GetLoadableTypes(assembly))
            {
                if (!IsRoot(type))
                {
                    continue;
                }

                string content = GenerateFile(type, out string rootTsName);
                string outputFile = Path.Combine(OutputPath, $"{ToCamelCase(rootTsName)}.ts");
                File.WriteAllText(outputFile, content, Encoding.UTF8);
                generatedFiles.Add(outputFile);
            }
        }

        GeneratedFiles = generatedFiles.Select(f => (ITaskItem)new TaskItem(f)).ToArray();
        return !Log.HasLoggedErrors;
    }

    private static bool IsRoot(Type type) =>
        type.GetCustomAttributesData().Any(a => a.AttributeType.FullName == TsInterfaceAttributeFullName);

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

    private string GenerateFile(Type rootType, out string rootName)
    {
        _rootAssembly = rootType.Assembly;
        _visited = new HashSet<Type>();
        _blocks = new List<string>();

        EmitType(rootType);

        rootName = GetTsName(rootType);

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// Generated by WasmBridge.Tasks. Do not edit by hand.");
        sb.AppendLine();
        foreach (string block in _blocks)
        {
            sb.Append(block);
        }
        sb.AppendLine("function deepFreeze<T>(value: T): Readonly<T> {");
        sb.AppendLine("  if (value !== null && typeof value === 'object' && !Object.isFrozen(value)) {");
        sb.AppendLine("    Object.values(value).forEach(deepFreeze)");
        sb.AppendLine("    Object.freeze(value)");
        sb.AppendLine("  }");
        sb.AppendLine();
        sb.AppendLine("  return value");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine($"export function parse{rootName}(json: string): {rootName} {{");
        sb.AppendLine($"  return deepFreeze(JSON.parse(json) as {rootName})");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private void EmitType(Type type)
    {
        if (!_visited.Add(type))
        {
            return;
        }

        string tsName = GetTsName(type);

        Type? dictionaryValueType = GetStringKeyedDictionaryValueType(type.BaseType);
        if (dictionaryValueType is not null)
        {
            string valueTs = ResolveTypeScriptType(dictionaryValueType, nullable: false);
            _blocks.Add($"export type {tsName} = Readonly<Record<string, {valueTs}>>\n\n");
            return;
        }

        string extendsClause = string.Empty;
        Type? baseType = type.BaseType;
        if (baseType is not null && baseType.FullName != "System.Object" && baseType.Assembly == _rootAssembly)
        {
            EmitType(baseType);
            extendsClause = $" extends {GetTsName(baseType)}";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"export interface {tsName}{extendsClause} {{");
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (property.GetIndexParameters().Length > 0 || property.GetGetMethod() is null)
            {
                continue;
            }

            bool nullable = IsNullableReference(property);
            string propertyTs = ResolveTypeScriptType(property.PropertyType, nullable);
            sb.AppendLine($"  readonly {property.Name}: {propertyTs}");
        }

        sb.AppendLine("}");
        sb.AppendLine();

        _blocks.Add(sb.ToString());
    }

    private string ResolveTypeScriptType(Type type, bool nullable)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition().FullName == "System.Nullable`1")
        {
            type = type.GetGenericArguments()[0];
            nullable = true;
        }

        string baseType = ResolveNonNullableTypeScriptType(type);
        return nullable ? $"{baseType} | null" : baseType;
    }

    private string ResolveNonNullableTypeScriptType(Type type)
    {
        if (type.FullName == "System.String")
        {
            return "string";
        }

        if (type.FullName == "System.Boolean")
        {
            return "boolean";
        }

        if (type.FullName is not null && NumericAliases.TryGetValue(type.FullName, out string? numericAlias))
        {
            return numericAlias;
        }

        if (type.IsEnum)
        {
            return "number";
        }

        if (type.IsArray)
        {
            string elementType = ResolveTypeScriptType(type.GetElementType()!, nullable: false);
            return $"readonly {elementType}[]";
        }

        if (type.IsGenericType)
        {
            Type genericDefinition = type.GetGenericTypeDefinition();
            string? genericDefinitionName = genericDefinition.FullName;
            Type[] genericArguments = type.GetGenericArguments();

            if (genericDefinitionName is not null && ListLikeGenericTypes.Contains(genericDefinitionName))
            {
                string elementType = ResolveTypeScriptType(genericArguments[0], nullable: false);
                return $"readonly {elementType}[]";
            }

            if (genericDefinitionName is not null && DictionaryLikeGenericTypes.Contains(genericDefinitionName)
                && genericArguments[0].FullName == "System.String")
            {
                string valueType = ResolveTypeScriptType(genericArguments[1], nullable: false);
                return $"Readonly<Record<string, {valueType}>>";
            }
        }

        if (type.Assembly == _rootAssembly)
        {
            EmitType(type);
            return GetTsName(type);
        }

        Log.LogError($"GenerateTypeScriptTypes: unsupported type '{type.FullName}' - add a mapping or a [WasmBridgeTsInterface] override.");
        return "unknown";
    }

    private string GetTsName(Type type) => type.Name;

    private static Type? GetStringKeyedDictionaryValueType(Type? baseType)
    {
        if (baseType is { IsGenericType: true } && baseType.GetGenericTypeDefinition().FullName == "System.Collections.Generic.Dictionary`2")
        {
            Type[] arguments = baseType.GetGenericArguments();
            if (arguments[0].FullName == "System.String")
            {
                return arguments[1];
            }
        }

        return null;
    }

    private static bool IsNullableReference(PropertyInfo property)
    {
        if (property.PropertyType.IsValueType)
        {
            return false;
        }

        CustomAttributeData? nullableAttribute = property.GetCustomAttributesData()
            .FirstOrDefault(a => a.AttributeType.FullName == NullableAttributeFullName);
        if (nullableAttribute is not null)
        {
            return GetNullableFlag(nullableAttribute) == 2;
        }

        CustomAttributeData? contextAttribute = property.DeclaringType?.GetCustomAttributesData()
            .FirstOrDefault(a => a.AttributeType.FullName == NullableContextAttributeFullName);
        if (contextAttribute is not null)
        {
            return GetNullableFlag(contextAttribute) == 2;
        }

        return false;
    }

    private static byte GetNullableFlag(CustomAttributeData attribute)
    {
        object? value = attribute.ConstructorArguments[0].Value;
        return value switch
        {
            byte flag => flag,
            ReadOnlyCollection<CustomAttributeTypedArgument> flags when flags.Count > 0 && flags[0].Value is byte first => first,
            _ => 0,
        };
    }

    private static string ToCamelCase(string name) =>
        name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);
}
