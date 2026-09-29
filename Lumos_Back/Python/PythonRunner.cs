using System.Runtime.InteropServices;
using Python.Runtime;

namespace Lumos.Python;

public static class PythonRunner {
    private static bool _initialized;
    private static Py.GILState? _gilState;
    private static dynamic? _pyModule;

    /// <summary>Highest CPython minor version the referenced Python.NET build carries an ABI for. Raise it when the pythonnet package is updated.</summary>
    private const int MaxSupportedMinorVersion = 13;

    /// <summary>Root directory of the virtual environment in use, or null when running against the system interpreter.</summary>
    public static string? VirtualEnvironment { get; private set; }

    public static void Setup() {
        if(_initialized) return;
        VirtualEnvironment = FindVirtualEnvironment();
        Dictionary<string, string> config = VirtualEnvironment is null ? [] : ReadVenvConfig(VirtualEnvironment);
        config.TryGetValue("home", out string? home);
        string? basePrefix = ResolveBasePrefix(home);
        int wantedMinor = ParseConfigMinorVersion(config);

        string path = FindPythonDll(basePrefix, wantedMinor) ?? throw new FileNotFoundException($"No Python shared library of version 3.{MaxSupportedMinorVersion} or lower was found. Set the PYTHONNET_PYDLL environment variable to the full path of the libpython3.x shared library.");
        Runtime.PythonDLL = path;
        if(basePrefix is not null) PythonEngine.PythonHome = basePrefix;

        // .NET 9 removed BinaryFormatter, which Python.NET otherwise uses to stash runtime data on shutdown.
        RuntimeData.FormatterType = typeof(NoopFormatter);
        PythonEngine.Initialize();
        _initialized = true;
        _gilState = Py.GIL();

        dynamic sys = Py.Import("sys");
        if(VirtualEnvironment is not null) ActivateVirtualEnvironment(sys, VirtualEnvironment);
        sys.path.append(Environment.CurrentDirectory);
        _pyModule = PyModule.Import(AiRunner.ModuleName);
    }

    /// <summary>
    /// Points the running interpreter at the virtual environment. A venv ships no interpreter of its own, so the
    /// base installation stays loaded and only the resolution paths are redirected at its site-packages.
    /// </summary>
    private static void ActivateVirtualEnvironment(dynamic sys, string venv) {
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        string executable = isWindows
            ? Path.Combine(venv, "Scripts", "python.exe")
            : Path.Combine(venv, "bin", "python");
        if(File.Exists(executable)) sys.executable = executable;
        sys.prefix = venv;
        sys.exec_prefix = venv;

        string? sitePackages = FindSitePackages(venv);
        if(sitePackages is null) return;
        sys.path.insert(0, sitePackages);
        dynamic site = Py.Import("site");
        site.addsitedir(sitePackages);
    }

    private static string? FindSitePackages(string venv) {
        if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            string windowsPath = Path.Combine(venv, "Lib", "site-packages");
            return Directory.Exists(windowsPath) ? windowsPath : null;
        }
        string library = Path.Combine(venv, "lib");
        if(!Directory.Exists(library)) return null;
        return Directory.EnumerateDirectories(library, "python3.*")
            .Select(directory => Path.Combine(directory, "site-packages"))
            .Where(Directory.Exists)
            .OrderByDescending(MinorVersion)
            .FirstOrDefault();
    }

    /// <summary>Locates a virtual environment from the environment variables, then by searching upwards from the working directory.</summary>
    private static string? FindVirtualEnvironment() {
        foreach(string variable in new[] { "LUMOS_PYTHON_VENV", "VIRTUAL_ENV" }) {
            string? value = Environment.GetEnvironmentVariable(variable);
            if(!string.IsNullOrWhiteSpace(value) && Directory.Exists(value)) return Path.GetFullPath(value);
        }
        foreach(string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory }) {
            DirectoryInfo? directory = new(start);
            while(directory is not null) {
                foreach(string name in new[] { ".venv", "venv", "env" }) {
                    string candidate = Path.Combine(directory.FullName, name);
                    if(File.Exists(Path.Combine(candidate, "pyvenv.cfg"))) return candidate;
                }
                directory = directory.Parent;
            }
        }
        return null;
    }

    private static Dictionary<string, string> ReadVenvConfig(string venv) {
        Dictionary<string, string> config = new(StringComparer.OrdinalIgnoreCase);
        string path = Path.Combine(venv, "pyvenv.cfg");
        if(!File.Exists(path)) return config;
        foreach(string line in File.ReadLines(path)) {
            int separator = line.IndexOf('=');
            if(separator <= 0) continue;
            config[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }
        return config;
    }

    /// <summary>Turns the <c>home</c> entry of pyvenv.cfg (the base interpreter's bin directory) into the base installation prefix.</summary>
    private static string? ResolveBasePrefix(string? home) {
        if(string.IsNullOrWhiteSpace(home) || !Directory.Exists(home)) return null;
        string name = Path.GetFileName(home.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        bool isBinDirectory = name.Equals("bin", StringComparison.OrdinalIgnoreCase) || name.Equals("Scripts", StringComparison.OrdinalIgnoreCase);
        return isBinDirectory ? Path.GetDirectoryName(home.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : home;
    }

    private static int ParseConfigMinorVersion(Dictionary<string, string> config) {
        if(!config.TryGetValue("version", out string? version) && !config.TryGetValue("version_info", out version)) return -1;
        string[] parts = version.Split('.');
        return parts.Length >= 2 && int.TryParse(parts[1], out int minor) ? minor : -1;
    }

    private static string? FindPythonDll(string? basePrefix, int wantedMinor) {
        string? overridePath = Environment.GetEnvironmentVariable("PYTHONNET_PYDLL");
        if(!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath)) return overridePath;
        if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return FindWindowsPythonDll(basePrefix, wantedMinor);
        return FindUnixPythonLibrary(basePrefix, wantedMinor);
    }

    private static string? FindUnixPythonLibrary(string? basePrefix, int wantedMinor) {
        bool isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        string extension = isMac ? ".dylib" : ".so";
        string architecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "aarch64-linux-gnu" : "x86_64-linux-gnu";
        List<string> directories = [];
        if(basePrefix is not null) {
            directories.Add(Path.Combine(basePrefix, "lib"));
            directories.Add(Path.Combine(basePrefix, "lib64"));
            directories.Add(Path.Combine(basePrefix, "lib", architecture));
        }
        directories.AddRange([
            "/usr/lib",
            $"/usr/lib/{architecture}",
            "/usr/lib64",
            "/usr/local/lib",
            $"/usr/local/lib/{architecture}",
            "/opt/homebrew/lib",
            "/usr/local/opt/python/lib"
        ]);
        List<string> candidates = [];
        foreach(string directory in directories) {
            if(!Directory.Exists(directory)) continue;
            candidates.AddRange(Directory.EnumerateFiles(directory, $"libpython3.*{extension}"));
            if(!isMac) candidates.AddRange(Directory.EnumerateFiles(directory, "libpython3.*.so.1.0"));
        }
        return SelectBestCandidate(candidates, wantedMinor, candidate => candidate.Contains(".so.1.0"));
    }

    private static string? FindWindowsPythonDll(string? basePrefix, int wantedMinor) {
        List<string> directories = [];
        if(basePrefix is not null && Directory.Exists(basePrefix)) directories.Add(basePrefix);
        foreach(string root in new[] {
                    "C:\\Program Files",
                    "C:\\Program Files (x86)",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python")
                }) {
            if(Directory.Exists(root)) directories.AddRange(Directory.GetDirectories(root));
        }
        List<string> candidates = [];
        foreach(string directory in directories) {
            if(!Directory.Exists(directory)) continue;
            candidates.AddRange(Directory.EnumerateFiles(directory, "python3*.dll")
                .Where(candidate => !Path.GetFileName(candidate).Equals("python3.dll", StringComparison.OrdinalIgnoreCase)));
        }
        return SelectBestCandidate(candidates, wantedMinor, _ => false);
    }

    /// <summary>Prefers the version the virtual environment was created with, and falls back to the newest installation.</summary>
    private static string? SelectBestCandidate(List<string> candidates, int wantedMinor, Func<string, bool> deprioritize) {
        List<string> valid = candidates.Where(candidate => MinorVersion(candidate) is >= 0 and <= MaxSupportedMinorVersion).Distinct().ToList();
        return valid.Where(candidate => wantedMinor >= 0 && MinorVersion(candidate) == wantedMinor)
            .OrderBy(deprioritize)
            .FirstOrDefault()
            ?? valid.OrderByDescending(MinorVersion).ThenBy(deprioritize).FirstOrDefault();
    }

    /// <summary>Extracts the minor version from names such as libpython3.12.so, libpython3.12.so.1.0, python312.dll or python3.12.</summary>
    private static int MinorVersion(string path) {
        string name = Path.GetFileName(path);
        int marker = name.IndexOf("3.", StringComparison.Ordinal);
        int start;
        if(marker >= 0) {
            start = marker + 2;
        } else {
            marker = name.IndexOf("3", StringComparison.Ordinal);
            if(marker < 0) return -1;
            start = marker + 1;
        }
        int end = start;
        while(end < name.Length && char.IsDigit(name[end])) end++;
        return end > start && int.TryParse(name[start..end], out int minor) ? minor : -1;
    }

    public static void Dispose() {
        _gilState?.Dispose();
        _gilState = null;
        if(!_initialized) return;
        PythonEngine.Shutdown();
        _initialized = false;
    }

    public static dynamic ImportModule(string moduleName) {
        return !_initialized ? throw new InvalidOperationException("Python engine is not initialized.") : Py.Import(moduleName);
    }
}
