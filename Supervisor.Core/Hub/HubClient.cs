using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Supervisor.Core.Fleet;

namespace Supervisor.Core.Hub;

/// <summary>
/// Typed client for a Hub's authenticated admin surface.
/// </summary>
public sealed class HubClient : IDisposable
{
    private readonly HubRendezvous _rendezvous;
    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public HubClient(HubRendezvous rendezvous, HttpClient? http = null)
    {
        ArgumentNullException.ThrowIfNull(rendezvous);
        _rendezvous = rendezvous;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _ownsClient = http is null;
    }

    public async Task<HubStatus?> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        using var request = Authorized(HttpMethod.Get, "/hub/status");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        return response.IsSuccessStatusCode
            ? await response.Content
                .ReadFromJsonAsync(HubApiJsonContext.Default.HubStatus, cancellationToken)
                .ConfigureAwait(false)
            : null;
    }

    /// <summary>
    /// Asks the Hub to stop. A refusal is a normal outcome, not an error — the caller decides
    /// whether to force.
    /// </summary>
    public async Task<HubStopResult> StopAsync(bool force, CancellationToken cancellationToken = default)
    {
        using var request = Authorized(HttpMethod.Post, $"/hub/stop?force={(force ? "true" : "false")}");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var body = await response.Content
            .ReadFromJsonAsync(HubApiJsonContext.Default.HubStopResponse, cancellationToken)
            .ConfigureAwait(false);

        return new HubStopResult(
            Refused: response.StatusCode == HttpStatusCode.Conflict,
            AttachedClients: body?.AttachedClients ?? 0);
    }

    /// <summary>
    /// Reads the Fleet as this Hub sees it.
    /// </summary>
    /// <remarks>
    /// Returns null when the Hub refuses or answers unusably, so a caller can distinguish "no Hub
    /// answer" from "a Hub that answered, with nothing running" — an empty fleet is a real result.
    /// </remarks>
    public async Task<FleetView?> GetFleetAsync(
        FleetQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var path = string.IsNullOrWhiteSpace(query.WorkingDirectory)
            ? "/hub/fleet"
            : "/hub/fleet?cwd=" + Uri.EscapeDataString(query.WorkingDirectory);

        using var request = Authorized(HttpMethod.Get, path);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        return response.IsSuccessStatusCode
            ? await response.Content
                .ReadFromJsonAsync(FleetJsonContext.Default.FleetView, cancellationToken)
                .ConfigureAwait(false)
            : null;
    }

    private HttpRequestMessage Authorized(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(new Uri(_rendezvous.Endpoint), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _rendezvous.HostSecret);
        return request;
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }
}

public readonly record struct HubStopResult(bool Refused, int AttachedClients);
