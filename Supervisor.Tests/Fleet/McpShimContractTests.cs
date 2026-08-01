using System.Diagnostics;
using System.Text.Json;

namespace Supervisor.Tests.Fleet;

/// <summary>
/// Speaks MCP to the built binary over stdio.
/// </summary>
/// <remarks>
/// <para>
/// Every other Fleet test exercises <c>FleetTool</c> directly, which proves the answer is right and
/// nothing about whether Claude Code can reach it. The handler registration, the tool schema, and
/// the JSON-RPC plumbing are all composition — exactly the layer that failed silently in WU-A, where
/// 31 green unit tests sat on top of a CLI that could not execute a single verb (friction F-7).
/// </para>
/// <para>
/// The shim also starts enrolling in the background. With no Claude session to find it gives up
/// quietly and never reaches the Hub, so this test starts no background processes of its own.
/// </para>
/// </remarks>
public sealed class McpShimContractTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task AdvertisesAndServesTheFleetToolOverStdio()
    {
        var exe = SupervisorBinary();
        Assert.True(File.Exists(exe), $"Supervisor binary not found at {exe} — build the solution first.");

        using var process = Start(exe);

        try
        {
            using var deadline = new CancellationTokenSource(Timeout);

            await SendAsync(process, new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new { name = "supervisor-tests", version = "1.0.0" },
                },
            });

            var initialize = await ReadResponseAsync(process, id: 1, deadline.Token);
            Assert.Equal(
                "thesupervisor",
                initialize.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());

            await SendAsync(process, new { jsonrpc = "2.0", method = "notifications/initialized" });

            await SendAsync(process, new { jsonrpc = "2.0", id = 2, method = "tools/list" });
            var tools = await ReadResponseAsync(process, id: 2, deadline.Token);

            var tool = Assert.Single(tools.GetProperty("result").GetProperty("tools").EnumerateArray());
            Assert.Equal("list_fleet", tool.GetProperty("name").GetString());

            // The schema is hand-written to keep it off the startup path, so nothing else would
            // notice if it stopped being valid JSON Schema shaped the way a client expects.
            var schema = tool.GetProperty("inputSchema");
            Assert.Equal("object", schema.GetProperty("type").GetString());
            Assert.True(schema.GetProperty("properties").TryGetProperty("cwd", out _));

            await SendAsync(process, new
            {
                jsonrpc = "2.0",
                id = 3,
                method = "tools/call",
                @params = new { name = "list_fleet", arguments = new { } },
            });

            var call = await ReadResponseAsync(process, id: 3, deadline.Token);
            var text = call.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();

            // No Hub is running for this test, so the honest answer is an empty fleet — and it must
            // arrive as parseable JSON rather than as an error the calling Agent would try to fix.
            var view = JsonDocument.Parse(text!);
            Assert.Empty(view.RootElement.GetProperty("agents").EnumerateArray());
        }
        finally
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        }
    }

    private static Process Start(string exe)
    {
        var info = new ProcessStartInfo(exe)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        info.ArgumentList.Add("mcp");

        return Process.Start(info)!;
    }

    private static async Task SendAsync(Process process, object message)
    {
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message));
        await process.StandardInput.FlushAsync();
    }

    /// <summary>Reads lines until the response with this id arrives, skipping notifications.</summary>
    private static async Task<JsonElement> ReadResponseAsync(
        Process process, int id, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await process.StandardOutput.ReadLineAsync(cancellationToken);

            if (line is null)
            {
                var error = await process.StandardError.ReadToEndAsync(CancellationToken.None);
                Assert.Fail($"The shim closed stdout before answering id {id}. stderr: {error}");
            }

            if (line.Length == 0)
            {
                continue;
            }

            var message = JsonDocument.Parse(line).RootElement;

            if (message.TryGetProperty("id", out var responseId)
                && responseId.ValueKind == JsonValueKind.Number
                && responseId.GetInt32() == id)
            {
                Assert.False(
                    message.TryGetProperty("error", out var failure),
                    $"id {id} returned an error: {failure}");

                return message.Clone();
            }
        }

        Assert.Fail($"Timed out waiting {Timeout.TotalSeconds:N0}s for a response to id {id}.");
        return default;
    }

    private static string SupervisorBinary()
    {
        var testDir = new DirectoryInfo(AppContext.BaseDirectory);
        var tfm = testDir.Name;
        var configuration = testDir.Parent!.Name;
        var repoRoot = testDir.Parent!.Parent!.Parent!.Parent!.FullName;

        var name = OperatingSystem.IsWindows() ? "supervisor.exe" : "supervisor";
        return Path.Combine(repoRoot, "Supervisor", "bin", configuration, tfm, name);
    }
}
