using System.Text.Json;

namespace Lumos;

public class Result {
    // TODO: implement result class
    public Result(dynamic result) {
        
    }

    public void Save(string path) {
        using FileStream fs = new(path, FileMode.Create);
        using Utf8JsonWriter writer = new(fs);
        writer.WriteStartObject();
        // TODO: write result properties
        writer.WriteEndObject();
        writer.Flush();
    }
}