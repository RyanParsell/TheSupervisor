using System.Net.Http.Headers;
using System.Net.Http.Json;
using Supervisor.Core.Hub;

namespace Supervisor.Core.Enrollment;

/// <summary>Outcome of an enrollment attempt.</summary>
public enum EnrollmentOutcome
{
    Enrolled,
    HubUnreachable,
    ProtocolMismatch,
    Refused,
}

public readonly record struct EnrollmentResult(EnrollmentOutcome Outcome, string? AgentId, string? Detail)
{
    public bool Succeeded => Outcome == EnrollmentOutcome.Enrolled;
}

/// <summary>
/// Enrols an Agent with the Hub.
/// </summary>
/// <remarks>
/// A seam, so the shim's fail-open behaviour is testable without a running Hub — which is the whole
/// point, since a real Hub is the one thing a test cannot assume.
/// </remarks>
public interface IAgentEnroller
{
    Task<EnrollmentResult> EnrolAsync(AgentRegistration registration, CancellationToken cancellationToken = default);
    Task DeregisterAsync(string machineId, string sessionId, CancellationToken cancellationToken = default);
}

/// <summary>Enrols over the Hub's authenticated loopback API.</summary>
public sealed class HttpAgentEnroller : IAgentEnroller, IDisposable
{
    private readonly HubRendezvous _rendezvous;
    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public HttpAgentEnroller(HubRendezvous rendezvous, HttpClient? http = null)
    {
        ArgumentNullException.ThrowIfNull(rendezvous);
        _rendezvous = rendezvous;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        _ownsClient = http is null;
    }

    public async Task<EnrollmentResult> EnrolAsync(
        AgentRegistration registration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, new Uri(new Uri(_rendezvous.Endpoint), "/hub/agents"))
            {
                Content = JsonContent.Create(
                    registration, EnrollmentJsonContext.Default.AgentRegistration),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _rendezvous.HostSecret);

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                return new EnrollmentResult(
                    EnrollmentOutcome.ProtocolMismatch, null,
                    $"Hub speaks a different enrollment protocol than this build (sent v{registration.ProtocolVersion}).");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new EnrollmentResult(
                    EnrollmentOutcome.Refused, null, $"Hub refused enrollment: HTTP {(int)response.StatusCode}.");
            }

            var body = await response.Content
                .ReadFromJsonAsync(EnrollmentJsonContext.Default.AgentRegistrationResponse, cancellationToken)
                .ConfigureAwait(false);

            return body is null
                ? new EnrollmentResult(EnrollmentOutcome.Refused, null, "Hub returned an empty registration response.")
                : new EnrollmentResult(EnrollmentOutcome.Enrolled, body.AgentId, null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return new EnrollmentResult(EnrollmentOutcome.HubUnreachable, null, e.Message);
        }
    }

    public async Task DeregisterAsync(
        string machineId, string sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Delete,
                new Uri(new Uri(_rendezvous.Endpoint), $"/hub/agents/{machineId}/{sessionId}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _rendezvous.HostSecret);

            using var _ = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            // Best effort. A Hub that cannot be told about a departing Agent will notice on its own;
            // failing here would turn a tidy-up into a visible error at session exit.
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
