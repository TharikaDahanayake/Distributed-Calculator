using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<JsonOptions>(options => {
    options.SerializerOptions.PropertyNamingPolicy = null;
});
var app = builder.Build();

// In-memory store for clocks
var clocks = new ConcurrentDictionary<string, Dictionary<string, int>>();

// Endpoint to receive updates
app.MapPost("/api/clock", (VectorClockUpdate update) => {
    clocks[update.NodeId] = update.Clock;
    return Results.Ok();
});


// Dashboard page
app.MapGet("/", () => {
    var rows = string.Join("", clocks.Select(kvp =>
        $"<tr><td>{kvp.Key}</td><td>{string.Join(", ", kvp.Value.Select(c => c.Key + ":" + c.Value))}</td></tr>"));
    var html = $@"
<html>
<head>
    <title>Vector Clock Dashboard</title>
    <meta http-equiv='refresh' content='2'>
</head>
<body>
    <h1>Vector Clocks</h1>
    <table border='1'>
        <tr><th>Node</th><th>Clock</th></tr>
        {rows}
    </table>
    <p>Auto-refreshes every 2 seconds.</p>
</body>
</html>";
    return Results.Content(html, "text/html");
});

app.Run();

record VectorClockUpdate(string NodeId, Dictionary<string, int> Clock);