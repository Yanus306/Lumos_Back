using System.Collections.Concurrent;
using System.Text.Json;

namespace Lumos.Python;

public class AiRunner {
    private static readonly ConcurrentQueue<RequestData> Queue = new();
    private static readonly Thread Thread;
    public const string ModuleName = "check";

    static AiRunner() {
        Thread = new Thread(ThreadStart) {
            Name = "Python Runner",
            IsBackground = true
        };
    }

    public static void Setup() {
        Thread.Start();
    }

    public static Task<bool> AnalyzeAsync(string path, string folder, string resultPath) {
        TaskCompletionSource<bool> tcs = new();
        RequestData data = new(path, folder, resultPath, tcs);
        AddQueue(data);
        return tcs.Task;
    }

    private static void AddQueue(RequestData data) {
        Queue.Enqueue(data);
        lock(Queue) Monitor.Pulse(Queue);
    }

    private static void ThreadStart() {
        PythonRunner.Setup();

        AppDomain.CurrentDomain.ProcessExit += (_, _) => { PythonRunner.Dispose(); };

        dynamic checker = PythonRunner.ImportModule(ModuleName);
        ConcurrentQueue<RequestData> queue = Queue;
        while(true) {
            try {
                if(queue.TryDequeue(out RequestData? data)) {
                    try {
                        object result = checker.check(data.Path[0]);
                        data.RunNext(result);
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

    private class RequestData(string path, string folder, string resultPath, TaskCompletionSource<bool> tcs) {
        public string[] Path = [path];
        public readonly TaskCompletionSource<bool> Tcs = tcs;
        private Result[] Results;

        public void RunNext(dynamic data) {
            int count = data.__len__();
            if(count == 0) {
                Task.Run(() => {
                    try {
                        File.WriteAllText(resultPath, "[]");
                        Tcs.SetResult(true);
                    } catch (Exception e) {
                        Tcs.SetException(e);
                    }
                });
                return;
            }

            Results = new Result[count];
            for(int i = 0; i < count; i++)
                Results[i] = new Result(data[i]);
            Task.Run(() => {
                try {
                    using(FileStream fs = new(resultPath, FileMode.Create)) {
                        using Utf8JsonWriter writer = new(fs);
                        writer.WriteStartObject();
                        writer.WriteNumber("riskLevel", Results.Length switch {
                            < 5 => 0,
                            < 15 => 1,
                            _ => 2
                        });
                        writer.WriteStartArray("results");
                        for(int i = 0; i < Results.Length; i++)
                            Results[i].Save(writer);
                        writer.WriteEndArray();
                        writer.WriteEndObject();
                    }
                    Tcs.SetResult(true);
                    Directory.Delete(folder, true);
                } catch (Exception e) {
                    Tcs.SetException(e);
                }
            });
        }
    }
}