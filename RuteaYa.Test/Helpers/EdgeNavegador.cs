using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace RuteaYa.Test.Helpers;

internal static class EdgeNavegador
{
    public static string Ejecutable => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe");

    public static async Task<string> EjecutarAsync(string url)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var perfil = Path.Combine(Path.GetTempPath(), "opencode", "RuteaYa-browser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(perfil);
        var start = new ProcessStartInfo(Ejecutable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "--headless=new", "--no-first-run", "--no-default-browser-check", "--disable-background-networking",
            "--disable-extensions", "--remote-debugging-address=127.0.0.1", $"--remote-debugging-port={port}",
            "--host-resolver-rules=MAP * ~NOTFOUND, EXCLUDE 127.0.0.1, EXCLUDE localhost", $"--user-data-dir={perfil}", url }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var http = new HttpClient();
        try
        {
            string? ws = null;
            while (ws == null)
            {
                limite.Token.ThrowIfCancellationRequested();
                try {
                    using var json = JsonDocument.Parse(await http.GetStringAsync($"http://127.0.0.1:{port}/json/list", limite.Token));
                    ws = json.RootElement.EnumerateArray().FirstOrDefault(p => p.GetProperty("type").GetString() == "page" && p.GetProperty("url").GetString()!.StartsWith(url))
                        .GetProperty("webSocketDebuggerUrl").GetString();
                }
                catch (Exception e) when (e is HttpRequestException or KeyNotFoundException or InvalidOperationException) { await Task.Delay(100, limite.Token); }
            }
            using var socket = new ClientWebSocket(); await socket.ConnectAsync(new Uri(ws), limite.Token);
            var id = 0;
            while (true)
            {
                var requestId = ++id;
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id = requestId, method = "Runtime.evaluate",
                    @params = new { expression = "window.resultadoPruebaDirecciones || ''", returnByValue = true } }));
                await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, limite.Token);
                using var buffer = new MemoryStream();
                while (true)
                {
                    var chunk = new byte[8192];
                    var result = await socket.ReceiveAsync(chunk.AsMemory(), limite.Token);
                    buffer.Write(chunk, 0, result.Count);
                    if (!result.EndOfMessage) continue;
                    using var message = JsonDocument.Parse(buffer.ToArray());
                    if (message.RootElement.TryGetProperty("id", out var responseId) && responseId.GetInt32() == requestId)
                    {
                        var value = message.RootElement.GetProperty("result").GetProperty("result").GetProperty("value").GetString();
                        if (!string.IsNullOrEmpty(value)) return value;
                        break;
                    }
                    buffer.SetLength(0);
                }
                await Task.Delay(100, limite.Token);
            }
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }
}
