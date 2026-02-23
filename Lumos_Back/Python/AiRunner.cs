using System.Collections.Concurrent;

namespace Lumos.Python;

public class AiRunner {
    private static readonly ConcurrentQueue<RequestData> Queue = new();
    private static Thread _thread;
    public const string ModuleName = "check"; // TODO: change module name
    
    public static void Setup() {
        _thread = new Thread(ThreadStart) {
            Name = "Python Runner",
            IsBackground = true
        };
        _thread.Start();
    }

    public static Task<bool> AnalyzeAsync(string path, string resultPath) {
        TaskCompletionSource<bool> tcs = new();
        RequestData data = new(path, resultPath, tcs);
        Queue.Enqueue(data);
        lock(Queue) Monitor.Pulse(Queue);
        return tcs.Task;
    }

    private static void ThreadStart() {
        PythonRunner.Setup();
        
        AppDomain.CurrentDomain.ProcessExit += (_, _) => {
            PythonRunner.Dispose();
        };
        
        dynamic checker = PythonRunner.ImportModule(ModuleName);
        ConcurrentQueue<RequestData> queue = Queue;
        while(true) {
            try {
                if(queue.TryDequeue(out RequestData? data)) {
                    try {
                        dynamic result = checker.check(data.Path);
                        Task.Run(() => {
                            try {
                                Result resultObj = new Result(result);
                                resultObj.Save(data.ResultPath);
                                data.Tcs.SetResult(true);
                            } catch (Exception e) {
                                data.Tcs.SetException(e);
                            }
                        });
                    } catch (Exception e) {
                        data.Tcs.SetException(e);
                    }
                } else {
                    lock(queue) Monitor.Wait(queue);
                }
            } catch (ThreadInterruptedException) {
                break;
            } catch (Exception e) {
                Console.WriteLine(e);
                break;
            }
        }
        PythonRunner.Dispose();
    }

    private class RequestData(string path, string resultPath, TaskCompletionSource<bool> tcs) {
        public readonly string Path = path;
        public readonly string ResultPath = resultPath;
        public readonly TaskCompletionSource<bool> Tcs = tcs;
    }
}