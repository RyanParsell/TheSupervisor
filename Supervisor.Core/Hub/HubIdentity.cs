using System.Security.Cryptography;

namespace Supervisor.Core.Hub;

/// <summary>
/// Generates the identifiers a Hub publishes at startup.
/// </summary>
public static class HubIdentity
{
    /// <summary>
    /// A non-secret, human-quotable id for this Hub instance. Safe to log and to show in errors.
    /// </summary>
    public static string NewHostId() => $"hub-{Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant()}";

    /// <summary>
    /// A 256-bit bearer secret authenticating administrative calls.
    /// </summary>
    /// <remarks>
    /// Read access to this value is equivalent to control of the Hub, which is why the rendezvous
    /// file's DACL is restricted and why it must never appear in argv, a URL, a log, telemetry, or a
    /// terminal transcript.
    /// </remarks>
    public static string NewHostSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    /// <summary>
    /// Compares a presented secret against the expected one in constant time.
    /// </summary>
    /// <remarks>
    /// Ordinary string comparison returns as soon as it finds a differing character, so the time it
    /// takes leaks how much of the prefix was right — enough, over many attempts against a loopback
    /// endpoint, to recover the secret a byte at a time.
    /// </remarks>
    public static bool SecretsMatch(string? presented, string expected)
    {
        if (presented is null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(presented),
            System.Text.Encoding.UTF8.GetBytes(expected));
    }
}
