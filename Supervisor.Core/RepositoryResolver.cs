using System.Text.RegularExpressions;

namespace Supervisor.Core;

/// <summary>
/// Identity of the repository an Agent is working in.
/// </summary>
/// <param name="Id">
/// Machine-independent when a git remote exists (<c>github.com/owner/repo</c>), so the same repo
/// checked out on two Machines is one identity. Falls back to <c>machine:&lt;id&gt;/&lt;path&gt;</c>.
/// </param>
/// <param name="DisplayPath">Where this checkout lives locally. Location, never identity.</param>
/// <param name="IsMachineLocal">True when no remote was found, so this identity cannot span Machines.</param>
public readonly record struct RepositoryIdentity(string Id, string DisplayPath, bool IsMachineLocal);

/// <summary>
/// Resolves a working directory to a <see cref="RepositoryIdentity"/>.
/// </summary>
/// <remarks>
/// Reads <c>.git/config</c> directly rather than shelling out to <c>git</c>. This runs on the
/// per-session fast path (NFR-2), where spawning a process would cost more than everything else the
/// shim does put together.
/// </remarks>
public static partial class RepositoryResolver
{
    public static RepositoryIdentity Resolve(
        string workingDirectory,
        string machineId,
        string? sshConfigPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(machineId);

        var full = Path.GetFullPath(workingDirectory);
        var remote = FindOriginUrl(full);

        if (remote is not null && Normalize(remote, sshConfigPath ?? DefaultSshConfigPath()) is { } normalized)
        {
            return new RepositoryIdentity(normalized, full, IsMachineLocal: false);
        }

        return new RepositoryIdentity($"machine:{machineId}/{full}", full, IsMachineLocal: true);
    }

    private static string DefaultSshConfigPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "config");

    /// <summary>
    /// Walks up from <paramref name="start"/> looking for <c>.git/config</c>, returning
    /// <c>remote.origin.url</c> if present.
    /// </summary>
    /// <remarks>
    /// Walking up matters: Agents routinely run in subdirectories, and resolving only at the repo
    /// root would file the same effort under two identities depending on where the session started.
    /// </remarks>
    private static string? FindOriginUrl(string start)
    {
        var directory = new DirectoryInfo(start);

        while (directory is not null)
        {
            var config = Path.Combine(directory.FullName, ".git", "config");
            if (File.Exists(config))
            {
                return ReadOriginUrl(config);
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string? ReadOriginUrl(string configPath)
    {
        try
        {
            var inOrigin = false;

            foreach (var raw in File.ReadLines(configPath))
            {
                var line = raw.Trim();

                if (line.StartsWith('['))
                {
                    inOrigin = line.Replace(" ", "", StringComparison.Ordinal)
                        .Equals("[remote\"origin\"]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (inOrigin && line.StartsWith("url", StringComparison.OrdinalIgnoreCase))
                {
                    var equals = line.IndexOf('=');
                    if (equals >= 0)
                    {
                        return line[(equals + 1)..].Trim();
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // An unreadable git config is not an error here — it just means we fall back to a
            // machine-local identity rather than failing enrollment.
        }

        return null;
    }

    /// <summary>
    /// Reduces every remote form to <c>host/owner/repo</c>.
    /// </summary>
    /// <remarks>
    /// SSH (<c>git@host:owner/repo.git</c>), HTTPS, and <c>ssh://</c> URLs all describe the same
    /// repository, and a developer may legitimately use different forms on different Machines —
    /// this project does exactly that. Treating them as distinct identities would split a Workstream
    /// in two the moment you switched machines.
    /// </remarks>
    internal static string? Normalize(string remoteUrl, string? sshConfigPath = null)
    {
        var url = remoteUrl.Trim();
        if (url.Length == 0)
        {
            return null;
        }

        // Order matters: the scp-like pattern happily matches "https://host/path" with host="https"
        // and path="//host/path", so an explicit scheme has to be ruled out first.
        string? body;
        if (url.Contains("://", StringComparison.Ordinal))
        {
            body = StripScheme(url);
        }
        else
        {
            var scpLike = ScpLikeRemote().Match(url);
            body = scpLike.Success
                ? $"{ResolveHost(scpLike.Groups["host"].Value, sshConfigPath)}/{scpLike.Groups["path"].Value}"
                : null;
        }

        if (body is null)
        {
            return null;
        }

        body = body.TrimEnd('/');
        if (body.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            body = body[..^4];
        }

        return body.Length == 0 ? null : body.ToLowerInvariant();
    }

    /// <summary>
    /// Maps an SSH <c>Host</c> alias to its real <c>HostName</c>.
    /// </summary>
    /// <remarks>
    /// A developer with two GitHub accounts commonly reaches one of them through an alias
    /// (<c>git@github-personal:owner/repo.git</c>). Taken literally, that produces a different
    /// identity than the same repository cloned over <c>github.com</c> elsewhere — splitting one
    /// effort into two Workstreams across Machines.
    /// <para>
    /// Only consulted when the host has no dot, which every real hostname does. That keeps
    /// <c>~/.ssh/config</c> off the per-session fast path in the common case (NFR-2).
    /// </para>
    /// </remarks>
    private static string ResolveHost(string host, string? sshConfigPath)
    {
        if (host.Contains('.', StringComparison.Ordinal) || sshConfigPath is null)
        {
            return host;
        }

        try
        {
            if (!File.Exists(sshConfigPath))
            {
                return host;
            }

            var inMatchingHost = false;

            foreach (var raw in File.ReadLines(sshConfigPath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                if (line.StartsWith("Host ", StringComparison.OrdinalIgnoreCase))
                {
                    var patterns = line[5..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    inMatchingHost = patterns.Any(p => p.Equals(host, StringComparison.OrdinalIgnoreCase));
                    continue;
                }

                if (inMatchingHost && line.StartsWith("HostName", StringComparison.OrdinalIgnoreCase))
                {
                    var value = line[8..].TrimStart('=', ' ', '\t').Trim();
                    if (value.Length > 0)
                    {
                        return value;
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // An unreadable ssh config just means we use the host as written.
        }

        return host;
    }

    private static string? StripScheme(string url)
    {
        var schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0)
        {
            return null;
        }

        var rest = url[(schemeEnd + 3)..];

        // Drop any userinfo (git@, token@) — the same repository is the same repository regardless
        // of who is authenticating to it.
        var at = rest.LastIndexOf('@');
        if (at >= 0)
        {
            rest = rest[(at + 1)..];
        }

        return rest.Length == 0 ? null : rest;
    }

    [GeneratedRegex(@"^(?:(?<user>[^@/]+)@)?(?<host>[^:/@]+):(?<path>.+)$", RegexOptions.ExplicitCapture)]
    private static partial Regex ScpLikeRemote();
}
