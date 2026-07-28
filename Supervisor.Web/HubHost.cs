using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Supervisor.Core;
using Supervisor.Core.Hub;

namespace Supervisor.Web;

/// <summary>
/// The Hub process: Kestrel on loopback, plus the rendezvous that lets launchers and shims find it.
/// </summary>
public sealed class HubHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly HubRendezvousStore _store;

    private HubHost(WebApplication app, HubRendezvousStore store, HubRendezvous rendezvous, HubLifecycle lifecycle)
    {
        _app = app;
        _store = store;
        Rendezvous = rendezvous;
        Lifecycle = lifecycle;
    }

    public HubRendezvous Rendezvous { get; }
    public HubLifecycle Lifecycle { get; }
    public string Endpoint => Rendezvous.Endpoint;

    /// <summary>Completes when the host shuts down, whether by signal or by <c>POST /hub/stop</c>.</summary>
    public Task WaitForShutdownAsync(CancellationToken cancellationToken = default) =>
        _app.WaitForShutdownAsync(cancellationToken);

    public static async Task<HubHost> StartAsync(
        HubRendezvousStore store,
        HubLifecycle? lifecycle = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        lifecycle ??= new HubLifecycle();

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();

        // Port 0 asks the OS for a free port. Loopback only — the Hub is never reachable from the
        // network, and federation (WU-5) will not change that: Peers talk Hub-to-Hub, never
        // Hub-to-someone-else's-Agent.
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(lifecycle);

        var startedAt = DateTimeOffset.UtcNow;
        var hostId = HubIdentity.NewHostId();
        var hostSecret = HubIdentity.NewHostSecret();

        var app = builder.Build();

        // Loopback is not authorization. Every process running as this user can reach 127.0.0.1,
        // and so can any page a browser can be induced to load, so the host secret is the whole
        // boundary between "on this machine" and "may control the Hub".
        //
        // Applied as middleware rather than per-endpoint so a route added later is protected by
        // default. Forgetting an attribute on one new endpoint is exactly how this kind of gate
        // rots.
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/hub"))
            {
                await next().ConfigureAwait(false);
                return;
            }

            const string scheme = "Bearer ";
            var header = context.Request.Headers.Authorization.ToString();
            var presented = header.StartsWith(scheme, StringComparison.Ordinal)
                ? header[scheme.Length..]
                : null;

            if (!HubIdentity.SecretsMatch(presented, hostSecret))
            {
                // No body and no detail: a caller without the secret learns only that it was
                // refused, never whether the Hub exists, its version, or how close the guess was.
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next().ConfigureAwait(false);
        });

        app.MapGet("/hub/status", (HubLifecycle live) => Results.Json(
            new HubStatus
            {
                HostId = hostId,
                BuildVersion = SupervisorVersion.Current,
                ProtocolVersion = SupervisorProtocol.EnrollmentVersion,
                AttachedClients = live.AttachedClients,
                StartedAt = startedAt,
            },
            HubApiJsonContext.Default.HubStatus));

        app.MapPost("/hub/stop", async (HubLifecycle live, bool? force) =>
        {
            var outcome = live.RequestStop(force ?? false);
            var body = new HubStopResponse
            {
                Outcome = outcome == HubStopOutcome.Stopped ? "stopped" : "refused-clients-attached",
                AttachedClients = live.AttachedClients,
            };

            if (outcome != HubStopOutcome.Stopped)
            {
                return Results.Json(body, HubApiJsonContext.Default.HubStopResponse, statusCode: StatusCodes.Status409Conflict);
            }

            // Reply before shutting down, so the caller learns the outcome rather than seeing a
            // dropped connection it cannot distinguish from a crash.
            _ = Task.Run(async () =>
            {
                await Task.Delay(50).ConfigureAwait(false);
                await app.StopAsync().ConfigureAwait(false);
            });

            return Results.Json(body, HubApiJsonContext.Default.HubStopResponse);
        });

        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        var endpoint = app.Urls.First();
        var rendezvous = new HubRendezvous
        {
            HostId = hostId,
            HostSecret = hostSecret,
            ProcessId = Environment.ProcessId,
            Endpoint = endpoint,
            BuildVersion = SupervisorVersion.Current,
            ProtocolVersion = SupervisorProtocol.EnrollmentVersion,
            StartedAt = startedAt,
        };

        store.Write(rendezvous);

        return new HubHost(app, store, rendezvous, lifecycle);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);

        // The rendezvous outliving the Hub is exactly what HubLocator has to clean up after, so
        // remove it on an orderly shutdown rather than leaving that work to the next caller.
        _store.Delete();
    }
}
