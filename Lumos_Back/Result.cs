using System.Text.Json;
using SixLabors.ImageSharp;

namespace Lumos;

public class Result {
    public Rectangle Rect;
    public string PatternType;
    public float YoloConfidence;
    
    public Result(dynamic result) {
        dynamic bbox = result["bbox"];
        Rect = new Rectangle((int)bbox[0], (int)bbox[1], (int)bbox[2], (int)bbox[3]);
        Rect.Width -= Rect.X;
        Rect.Height -= Rect.Y;
        PatternType = (string) result["class_name"];
        YoloConfidence = (float) result["confidence"];
    }

    public void Save(Utf8JsonWriter writer) {
        writer.WriteStartObject();
        
        writer.WriteStartArray("rect");
        writer.WriteNumberValue(Rect.X);
        writer.WriteNumberValue(Rect.Y);
        writer.WriteNumberValue(Rect.Width);
        writer.WriteNumberValue(Rect.Height);
        writer.WriteEndArray();
        
        writer.WriteString("patternType", PatternType);
        writer.WriteNumber("yoloConfidence", YoloConfidence);
        
        writer.WriteEndObject();
    }
}