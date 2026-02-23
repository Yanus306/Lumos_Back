using System.Collections.Concurrent;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

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
                        object result = data.IsNotFirst ? checker.calculate_risk(data.Path) : checker.check(data.Path[0]);
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
        public bool IsNotFirst;

        public void RunNext(dynamic data) {
            if(IsNotFirst) {
                for(int i = 0; i < Results.Length; i++) 
                    Results[i].ApplyAfter(data[i]);
                Task.Run(() => {
                    try {
                        using(FileStream fs = new(resultPath, FileMode.Create)) {
                            using Utf8JsonWriter writer = new(fs);
                            writer.WriteStartArray();
                            for(int i = 0; i < Results.Length; i++)
                                Results[i].Save(writer);
                            writer.WriteEndArray();
                        }
                        Tcs.SetResult(true);
                        Directory.Delete(folder, true);
                    } catch (Exception e) {
                        Tcs.SetException(e);
                    }
                });
            } else {
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
                        string path = Path[0];
                        Path = new string[count];

                        if(!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                        using(Image image = Image.Load(path)) {
                            for(int i = 0; i < Results.Length; i++) {
                                string imagePath = System.IO.Path.Combine(folder, $"{i}.jpg");
                                int i1 = i;
                                using(Image cropped = image.Clone(x => x.Crop(Results[i1].Rect)))
                                    cropped.Save(imagePath);
                                Path[i] = imagePath;
                            }
                        }

                        IsNotFirst = true;
                        AddQueue(this);
                    } catch (Exception e) {
                        Tcs.SetException(e);
                    }
                });
            }
        }
    }
}