using Python.Runtime;

namespace Lumos.Python;

public static class PythonRunner {
    private static bool _initialized;
    private static Py.GILState? _gilState;
    private static dynamic? _pyModule;

    public static void Setup() {
        if(_initialized) return;
        foreach(string directory in Directory.GetDirectories("C:\\Program Files").Concat(Directory.GetDirectories("C:\\Program Files (x86)")
                    .Concat(Directory.GetDirectories(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python"))))) {
            if(directory.Contains("Python")) {
                string path = Path.Combine(directory, Path.GetFileName(directory).ToLower() + ".dll");
                if(File.Exists(path)) {
                    Runtime.PythonDLL = path;
                    PythonEngine.Initialize();
                    _initialized = true;
                    _gilState = Py.GIL();
                    dynamic sys = Py.Import("sys");
                    sys.path.append(Environment.CurrentDirectory);
                    _pyModule = PyModule.Import(AiRunner.ModuleName);
                    return;
                }
            }
        }
        throw new FileNotFoundException("Python DLL not found in Program Files directories.");
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