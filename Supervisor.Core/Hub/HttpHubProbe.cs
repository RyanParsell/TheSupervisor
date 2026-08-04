using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Supervisor.Core.Hub;

/// <summary>
/// Verifies a Hub is alive by calling its authenticated status endpoint.
/// </summary>
public sealed class HttpHubProbe : IHubProbe, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public HttpHubProbe(HttpClient? http = null)
    {
        // Short timeout on purpose: this runs on the per-session fast path, and a Hub that cannot
        // answer promptly on loopback is one we should treat as gone rather than wait for.
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        _ownsClient = http is null;
    }

    /// <summary>
    /// True only when a Hub is reachable at the recorded endpoint <em>and</em> it is the same Hub,
    /// speaking a contract we understand.
    /// </summary>
    /// <remarks>
    /// Reachability alone is not enough. Ports are reused freely by the OS, so a 200 at the recorded
    /// endpoint may be an unrelated Hub — attaching to it would silently mix two Hubs' state.
    /// Protocol version is checked for the same reason it exists (D28): attaching across a
    /// mismatch means speaking a contract the other side does not implement, and the failure
    /// surfaces far from its cause.
    /// </remarks>
    public async Task<bool> IsAliveAsync(HubRendezvous rendezvous, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rendezvous);

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get, new Uri(new Uri(rendezvous.Endpoint), "/hub/status"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rendezvous.HostSecret);

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var status = await response.Content
                .ReadFromJsonAsync(HubApiJsonContext.Default.HubStatus, cancellationToken)
                .ConfigureAwait(false);

            if (status is null)
            {
                return false;
            }

            return status.HostId == rendezvous.HostId
                && status.ProtocolVersion == rendezvous.ProtocolVersion;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or UriFormatException or System.Text.Json.JsonException)
        {
            // Unreachable, refused, timed out, malformed endpoint, or an unparseable reply all mean
            // the same thing to a caller: there is no Hub here. Never throw at someone who only
            // asked whether one exists.
            return false;
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }
}
