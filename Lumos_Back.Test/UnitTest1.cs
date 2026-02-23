using System.Net.Http.Headers;

namespace Lumos_Back.Test;

public class Tests {
    [Test]
    public async Task SendCheckRequest_WithTestJpg() {
        const string testImagePath = "test.jpg";
        
        if(!File.Exists(testImagePath)) 
            throw new FileNotFoundException($"{testImagePath} is not exist.");

        await using FileStream fileStream = File.OpenRead(testImagePath);
        using StreamContent content = new(fileStream);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

        using HttpClient client = new();
        client.BaseAddress = new Uri("http://localhost:5000");

        HttpResponseMessage response = await client.PostAsync("/check", content);

        Assert.That(response.IsSuccessStatusCode, $"Request failed with status code {response.StatusCode}({(int) response.StatusCode})");
        
        string responseContent = await response.Content.ReadAsStringAsync();
        Assert.That(responseContent, Is.Not.Null.And.Not.Empty, "Response content is empty");
    }
}