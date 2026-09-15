using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var store = new SmsStore();

app.MapGet("/health", () => Results.Ok("Healthy"));

// Generic "send an SMS" endpoint. Mirrors the shape most REST SMS gateways use (to/from/message in, a message id back),
// so MockSmsSender in Sacco.Modules.Notifications can stand in for any of the real providers without special-casing.
// Simulate a gateway rejection with header `X-Simulate-Error: true`, or by sending to a number ending "0000".
app.MapPost("/sms/send", (SendSmsRequest req, HttpRequest http) =>
{
    if (string.IsNullOrWhiteSpace(req.To) || string.IsNullOrWhiteSpace(req.Message))
        return Results.BadRequest(new { error = "'to' and 'message' are required." });

    var simulateFailure = http.Headers.TryGetValue("X-Simulate-Error", out var v) && string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
        || req.To.TrimEnd().EndsWith("0000", StringComparison.Ordinal);
    var record = new SmsRecord(Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), req.To, req.From, req.Message,
        simulateFailure ? "Failed" : "Delivered", (req.Message.Length / 160) + 1, DateTimeOffset.UtcNow);
    store.Add(record);

    return simulateFailure
        ? Results.Json(new { messageId = (string?)null, status = record.Status, error = "Simulated gateway failure" }, statusCode: 422)
        : Results.Ok(new { messageId = record.MessageId, status = record.Status, to = record.To, segments = record.Segments });
});

app.MapGet("/api/messages", (int page = 1, int pageSize = 50) =>
{
    pageSize = pageSize <= 0 ? 50 : Math.Min(pageSize, 200);
    page = page <= 0 ? 1 : page;
    var all = store.All();
    var items = all.Skip((page - 1) * pageSize).Take(pageSize);
    return Results.Ok(new { items, total = all.Count, page, pageSize });
});

app.MapGet("/api/messages/{id}", (string id) => store.Find(id) is { } m ? Results.Ok(m) : Results.NotFound());

app.MapDelete("/api/messages", () => { store.Clear(); return Results.Ok(new { cleared = true }); });

app.MapGet("/", () => Results.Content(InboxPage.Html, "text/html"));

app.Run();

sealed record SendSmsRequest(string To, string? From, string Message);
sealed record SmsRecord(string MessageId, string To, string? From, string Message, string Status, int Segments, DateTimeOffset SentAt);

sealed class SmsStore
{
    private readonly ConcurrentStack<SmsRecord> _messages = new();
    public void Add(SmsRecord m) => _messages.Push(m);
    public List<SmsRecord> All() => [.. _messages];
    public SmsRecord? Find(string id) => _messages.FirstOrDefault(m => m.MessageId == id);
    public void Clear() => _messages.Clear();
}

static class InboxPage
{
    // A Mailpit-style inbox for SMS: no build step, polls the JSON API every 3s. Purely a local dev/demo convenience.
    public const string Html = """
    <!doctype html>
    <html>
    <head>
    <meta charset="utf-8">
    <title>Mock SMS gateway</title>
    <style>
      body { font-family: -apple-system, system-ui, sans-serif; margin: 0; background: #0b0f14; color: #e6edf3; }
      header { padding: 16px 24px; border-bottom: 1px solid #1f2937; display: flex; align-items: center; justify-content: space-between; }
      header h1 { font-size: 16px; margin: 0; font-weight: 600; }
      header button { background: #1f2937; color: #e6edf3; border: none; padding: 6px 12px; border-radius: 6px; cursor: pointer; }
      header button:hover { background: #374151; }
      table { width: 100%; border-collapse: collapse; }
      th, td { text-align: left; padding: 10px 24px; border-bottom: 1px solid #1f2937; font-size: 13px; vertical-align: top; }
      th { color: #8b98a5; font-weight: 500; position: sticky; top: 49px; background: #0b0f14; }
      .status-Delivered { color: #3fb950; }
      .status-Failed { color: #f85149; }
      .empty { padding: 48px 24px; color: #8b98a5; text-align: center; }
      .muted { color: #8b98a5; font-size: 12px; }
    </style>
    </head>
    <body>
    <header><h1>📱 Mock SMS gateway — sent messages</h1><button onclick="clearAll()">Clear all</button></header>
    <div id="root"></div>
    <script>
      async function load() {
        const res = await fetch('/api/messages?pageSize=200');
        const data = await res.json();
        const root = document.getElementById('root');
        if (!data.items.length) { root.innerHTML = '<div class="empty">No messages sent yet.</div>'; return; }
        root.innerHTML = '<table><thead><tr><th>Time</th><th>To</th><th>Message</th><th>Status</th><th>Message ID</th></tr></thead><tbody>' +
          data.items.map(m => `<tr><td class="muted">${new Date(m.sentAt).toLocaleString()}</td><td>${m.to}</td><td>${escapeHtml(m.message)}</td><td class="status-${m.status}">${m.status}</td><td class="muted">${m.messageId}</td></tr>`).join('') +
          '</tbody></table>';
      }
      function escapeHtml(s) { return s.replace(/[&<>]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;'}[c])); }
      async function clearAll() { await fetch('/api/messages', { method: 'DELETE' }); load(); }
      load();
      setInterval(load, 3000);
    </script>
    </body>
    </html>
    """;
}
