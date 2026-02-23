using System.Security.Cryptography;
using Lumos.Python;

namespace Lumos;

public static class Program {
    public const string ImageTempFolder = "ImageTemp";
    public const string ResultFolder = "Results";
    
    public static void Main(string[] args) {
        AiRunner.Setup();
        
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        WebApplication app = builder.Build();

        app.MapPost("/check", Check);

        app.Run();
    }

    public static async Task<IResult> Check(HttpContext context) {
        Guid requestId = Guid.NewGuid();
        string path = Path.Combine(ImageTempFolder, $"{requestId}.png");
        string resultPath;
        await using(FileStream fs = new(path, FileMode.Create)) {
            await context.Request.Body.CopyToAsync(fs);
            fs.Flush();
            fs.Position = 0;
            byte[] hash = await SHA256.HashDataAsync(fs);
            resultPath = Path.Combine(ResultFolder, $"{Convert.ToHexString(hash)}.json");
        }
        if(!File.Exists(resultPath)) await AiRunner.AnalyzeAsync(path, resultPath);
        
        return Results.File(resultPath, "application/json");
    }
}