using System.Text;
using System.Text.Json;

namespace CoachMate.Ai;

/// <summary>Minimal Ollama /api/chat client with timeout + offline fallback.</summary>
public sealed class OllamaClient
{
    public string Endpoint = "http://localhost:11434";
    public string Model = "qwen2.5:7b";
    private readonly HttpClient _http = new();

    public async Task<string?> ChatAsync(string system, string user)
    {
        try
        {
            var body = JsonSerializer.Serialize(new
            {
                model = Model,
                stream = false,
                messages = new[]
                {
                    new { role = "system", content = system },
                    new { role = "user", content = user }
                }
            });
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var res = await _http.PostAsync($"{Endpoint}/api/chat",
                new StringContent(body, Encoding.UTF8, "application/json"), cts.Token);
            if (!res.IsSuccessStatusCode)
                return $"Ollama error {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}";
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            return doc.RootElement.GetProperty("message").GetProperty("content").GetString()?.Trim();
        }
        catch { return null; }
    }
}
