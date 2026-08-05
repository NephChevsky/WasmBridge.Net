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

    /// <summary>
    /// Template for the loader function appended after the interface in each generated
    /// bridge file - injects a &lt;script type="module"&gt; that loads the published
    /// <c>dotnet.js</c> runtime, waits for it to publish the bridge class onto
    /// <c>window</c>, and resolves a promise with it. Mirrors the hand-written loader
    /// pattern previously used by consuming front-ends (e.g. `gameEngine.ts`/`todoBridge.ts`).
    /// Placeholders (__CLASSNAME__, __WINDOWPROP__, __PROMISEVAR__, __FUNCNAME__,
    /// __EVENTNAME__) are substituted per bridge type in <see cref="AppendLoader"/>.
    /// </summary>
    private const string LoaderTemplate = @"declare global {
  interface Window {
    __WINDOWPROP__?: __CLASSNAME__
  }
}

// Module-level singleton so double invocation (e.g. React StrictMode's double effect
// invocation in dev, or Vite HMR) doesn't inject the loader <script> twice and load the
// wasm runtime twice.
let __PROMISEVAR__: Promise<__CLASSNAME__> | null = null

export function __FUNCNAME__(): Promise<__CLASSNAME__> {
  if (!__PROMISEVAR__) {
    __PROMISEVAR__ = new Promise<__CLASSNAME__>((resolve) => {
      const onReady = () => {
        window.removeEventListener('__EVENTNAME__', onReady)
        resolve(window.__WINDOWPROP__!)
      }
      window.addEventListener('__EVENTNAME__', onReady)

      // Vite serves files under `public/` as static assets only, so the published
      // dotnet.js module must be loaded via a real <script type='module'> tag (an HTML
      // reference) rather than a JS `import()` call.
      const script = document.createElement('script')
      const debugLevel = import.meta.env.DEV ? 1 : 0
      script.type = 'module'
      script.textContent = `
        import { dotnet } from '/wasm-app/_framework/dotnet.js';
        const { getAssemblyExports, getConfig } = await dotnet
          .withDebugging(${debugLevel})
          .create();
        const config = getConfig();
        const exports = await getAssemblyExports(config.mainAssemblyName);
        window.__WINDOWPROP__ = exports.__CLASSNAME__;
        window.dispatchEvent(new Event('__EVENTNAME__'));
      `
      document.head.appendChild(script)
    })
  }
  return __PROMISEVAR__
}
";

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
            string returnType = GetBridgeReturnType(method.ReturnType);
            sb.AppendLine($"  {exportedName}: ({parameters}) => {returnType}");
        }

        sb.AppendLine("}");
        sb.AppendLine();
        AppendLoader(sb, generatedClassName);

        string fileName = ToCamelCase(generatedClassName);
        string filePath = Path.Combine(OutputPath, $"{fileName}.ts");
        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        return filePath;
    }

    /// <summary>
    /// Appends a module-level singleton loader function (see <see cref="LoaderTemplate"/>)
    /// for <paramref name="generatedClassName"/>, deriving its function/event/window-property
    /// names from the bridge class name with its trailing "Bridge" suffix stripped (e.g.
    /// "GameEngineBridge" -> "loadGameEngine", event "gameengine-ready", window property
    /// "__gameEngineBridge").
    /// </summary>
    private static void AppendLoader(StringBuilder sb, string generatedClassName)
    {
        string prefix = generatedClassName.EndsWith("Bridge", StringComparison.Ordinal) && generatedClassName.Length > "Bridge".Length
            ? generatedClassName.Substring(0, generatedClassName.Length - "Bridge".Length)
            : generatedClassName;
        string camelPrefix = ToCamelCase(prefix);

        string loader = LoaderTemplate
            .Replace("__CLASSNAME__", generatedClassName)
            .Replace("__WINDOWPROP__", "__" + camelPrefix + "Bridge")
            .Replace("__PROMISEVAR__", camelPrefix + "Promise")
            .Replace("__FUNCNAME__", "load" + prefix)
            .Replace("__EVENTNAME__", prefix.ToLowerInvariant() + "-ready");

        sb.Append(loader);
    }

    private string GetTypeScriptType(Type type)
    {
        if (type.FullName is not null && TypeScriptAliases.TryGetValue(type.FullName, out string? alias))
        {
            return alias;
        }

        if (type.IsArray)
        {
            Type? elementType = type.GetElementType();
            if (elementType is not null)
            {
                return $"{GetTypeScriptType(elementType)}[]";
            }
        }

        Log.LogError(
            $"GenerateBridgeTypeScript: unsupported type '{type.FullName}' used as a [WasmBridgeExport] " +
            "parameter or return type - only primitives, void, arrays of primitives, and " +
            "[WasmBridgeTsInterface]-rooted types (or a List<T> of one, for return types) are supported.");
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

    /// <summary>
    /// Maps a <c>[WasmBridgeExport]</c> method's return type to the TypeScript type the
    /// generated bridge method actually resolves to - mirroring
    /// <c>GenerateWasmBridgesTask</c>'s own return-shape rules: <c>Task</c>/<c>Task&lt;T&gt;</c>
    /// become <c>Promise&lt;...&gt;</c> (awaited on the JS side automatically since the
    /// generated C# bridge method itself returns a <c>Task</c>), and any
    /// <c>[WasmBridgeTsInterface]</c>-rooted result (whether awaited or not) becomes
    /// <c>string</c> since it's JSON-serialized by the bridge.
    /// </summary>
    private string GetBridgeReturnType(Type type)
    {
        if (TryUnwrapTask(type, out Type innerType, out bool isVoidTask))
        {
            if (isVoidTask)
            {
                return "Promise<void>";
            }

            string innerTsType = IsJsonSerialized(innerType) ? "string" : GetTypeScriptType(innerType);
            return $"Promise<{innerTsType}>";
        }

        return IsJsonSerialized(type) ? "string" : GetTypeScriptType(type);
    }

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

    private static string ToCamelCase(string name) =>
        name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);
}
