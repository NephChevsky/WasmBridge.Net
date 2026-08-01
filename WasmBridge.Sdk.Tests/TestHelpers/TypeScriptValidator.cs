using System.Diagnostics;

namespace WasmBridge.Sdk.Tests.TestHelpers;

/// <summary>
/// Shells out to a local <c>typescript</c> npm install (see <c>TypeScript/package.json</c>,
/// copied next to the test binaries) to actually type-check and execute the <c>.ts</c> files
/// produced by WasmBridge.Sdk's generator tasks - this is the only way to be sure a generated
/// file is valid TypeScript and behaves correctly at runtime, rather than just eyeballing the
/// emitted string.
/// </summary>
public static class TypeScriptValidator
{
    private static readonly object InstallLock = new();
    private static bool _installed;

    private static string ToolsDir => Path.Combine(AppContext.BaseDirectory, "TypeScript");

    private static string TscPath => Path.Combine(ToolsDir, "node_modules", ".bin", OperatingSystem.IsWindows() ? "tsc.cmd" : "tsc");

    /// <summary>Installs the local `typescript` package (once per test run) if not already present.</summary>
    private static void EnsureInstalled()
    {
        if (_installed)
        {
            return;
        }

        lock (InstallLock)
        {
            if (_installed)
            {
                return;
            }

            if (!File.Exists(TscPath))
            {
                RunProcess("npm", new[] { "install", "--no-audit", "--no-fund" }, ToolsDir, timeoutSeconds: 180);
            }

            if (!File.Exists(TscPath))
            {
                throw new InvalidOperationException(
                    $"Could not find the local TypeScript compiler at '{TscPath}' after 'npm install'. " +
                    "Make sure npm is on PATH and network access is available.");
            }

            _installed = true;
        }
    }

    /// <summary>
    /// Type-checks <paramref name="tsFilePath"/> with the TypeScript compiler in strict mode
    /// (<c>tsc --noEmit --strict</c>). Throws with the compiler's diagnostics if the file
    /// does not type-check cleanly.
    /// </summary>
    /// <param name="includeDom">Whether to include the "dom" lib (needed for files that reference `window`/`document`, e.g. bridge loader files).</param>
    public static void AssertValidTypeScript(string tsFilePath, bool includeDom = false)
    {
        EnsureInstalled();

        string lib = includeDom ? "es2020,dom" : "es2020";
        var args = new List<string> { "--noEmit", "--strict", "--target", "es2020", "--module", "es2020", "--moduleResolution", "bundler", "--lib", lib };
        if (includeDom)
        {
            // Bridge loader files reference `import.meta.env.DEV`, which needs the ambient
            // "vite/client" `ImportMeta.env` augmentation - see vite-client-shim.d.ts.
            args.Add(Path.Combine(ToolsDir, "vite-client-shim.d.ts"));
        }
        args.Add(tsFilePath);

        (int exitCode, string output) = RunProcess(TscPath, args, Path.GetDirectoryName(tsFilePath)!);

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Generated TypeScript file '{tsFilePath}' failed to type-check:\n{output}");
        }
    }

    /// <summary>
    /// Compiles <paramref name="tsFilePath"/> to CommonJS JavaScript, calls the exported
    /// function <paramref name="functionName"/> with <paramref name="argument"/>, and returns
    /// whatever it returned, JSON-serialized via <c>JSON.stringify</c>. Used to verify that
    /// a JSON payload produced by C#'s <c>System.Text.Json</c> round-trips correctly through
    /// the generated <c>parseX</c> function.
    /// </summary>
    public static string RunExportedFunction(string tsFilePath, string functionName, string argument)
    {
        EnsureInstalled();

        string workDir = TestPaths.CreateScratchDirectory();
        try
        {
            (int exitCode, string output) = RunProcess(
                TscPath,
                new[] { "--target", "es2020", "--module", "commonjs", "--moduleResolution", "node", "--outDir", workDir, tsFilePath },
                Path.GetDirectoryName(tsFilePath)!);
            if (exitCode != 0)
            {
                throw new InvalidOperationException($"Failed to compile '{tsFilePath}' to JavaScript:\n{output}");
            }

            string jsFilePath = Path.Combine(workDir, Path.GetFileNameWithoutExtension(tsFilePath) + ".js");

            const string script =
                "const mod = require(process.argv[1]);" +
                "const fn = mod[process.argv[2]];" +
                "if (typeof fn !== 'function') { console.error('export not found: ' + process.argv[2]); process.exit(2); }" +
                "const result = fn(process.argv[3]);" +
                "console.log(JSON.stringify(result));";

            (int runExitCode, string runOutput) = RunProcess(
                "node",
                new[] { "-e", script, jsFilePath, functionName, argument },
                workDir);

            if (runExitCode != 0)
            {
                throw new InvalidOperationException($"Running '{functionName}' from '{jsFilePath}' failed:\n{runOutput}");
            }

            return runOutput.Trim();
        }
        finally
        {
            try
            {
                Directory.Delete(workDir, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }

    private static (int ExitCode, string Output) RunProcess(string fileName, IEnumerable<string> arguments, string workingDirectory, int timeoutSeconds = 60)
    {
        var argumentsList = arguments as IList<string> ?? arguments.ToList();

        // "npm" (and the local tsc install) are ".cmd" shims on Windows - there's no npm.exe,
        // and Process.Start with UseShellExecute=false does not do PATHEXT-based resolution
        // the way cmd.exe / Explorer would, so starting "npm" directly fails with
        // Win32Exception "the system cannot find the file specified". Route these through
        // cmd.exe instead, which does resolve them.
        bool isCmdShim = OperatingSystem.IsWindows() &&
            (fileName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || fileName is "npm" or "npx");
        var startInfo = new ProcessStartInfo(isCmdShim ? "cmd.exe" : fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (isCmdShim)
        {
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(fileName);
        }

        foreach (string arg in argumentsList)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(timeoutSeconds * 1000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"'{fileName} {string.Join(' ', argumentsList)}' did not complete within {timeoutSeconds}s.");
        }

        return (process.ExitCode, stdout + stderr);
    }
}
