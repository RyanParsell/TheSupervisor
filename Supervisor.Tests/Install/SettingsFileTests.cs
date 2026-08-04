using Supervisor.Core.Install;

namespace Supervisor.Tests.Install;

public sealed class SettingsFileTests
{
    /// <summary>
    /// Mirrors the shape of the real hand-maintained file: two-space indent, several unrelated
    /// top-level keys, and — critically — <b>no</b> <c>hooks</c> key. That is the state a first
    /// install actually meets, so it is the state the tests are written against.
    /// </summary>
    private const string HandMaintained = """
        {
          "env": {
            "CLAUDE_CODE_USE_POWERSHELL_TOOL": "1"
          },
          "permissions": {
            "allow": [
              "Bash(dotnet *)",
              "Bash(git *)"
            ],
            "defaultMode": "acceptEdits"
          },
          "statusLine": {
            "type": "command",
            "command": "bash /c/Users/ryanp/.claude/statusline-command.sh"
          },
          "enabledPlugins": {
            "frontend-design@claude-plugins-official": true
          },
          "tui": "fullscreen"
        }
        """;

    private const string Command = @"""C:\Tools\supervisor.exe""";

    private static SettingsFile Load(string text) => SettingsFile.Parse(text, Command);

    [Fact]
    public void WritesOnlyOurMarkedBlock()
    {
        // The one thing that must never happen: touching anything the developer wrote. Compared
        // line by line rather than by parsing, because a reformat is semantically identical and
        // still destroys a hand-maintained file.
        //
        // The single permitted change is a trailing comma on what used to be the last property —
        // JSON requires it once something follows, and there is no way to add a key without it.
        // That it is the *only* change is what `UninstallRestoresTheFileByteIdentically` then pins.
        var installed = Load(HandMaintained).Install();
        var lines = installed.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        foreach (var line in HandMaintained.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Trim() is "{" or "}" or "")
            {
                continue;
            }

            Assert.True(
                lines.Contains(trimmed) || lines.Contains(trimmed + ","),
                $"line was altered by install: {trimmed}");
        }
    }

    [Fact]
    public void TheResultIsStillValidJsonWithEveryOriginalKey()
    {
        var installed = Load(HandMaintained).Install();

        using var document = System.Text.Json.JsonDocument.Parse(installed);
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("env", out _));
        Assert.True(root.TryGetProperty("permissions", out _));
        Assert.True(root.TryGetProperty("statusLine", out _));
        Assert.True(root.TryGetProperty("enabledPlugins", out _));
        Assert.Equal("fullscreen", root.GetProperty("tui").GetString());
        Assert.True(root.TryGetProperty("hooks", out _));
    }

    [Fact]
    public void RegistersBothLifecycleEvents()
    {
        var installed = Load(HandMaintained).Install();

        using var document = System.Text.Json.JsonDocument.Parse(installed);
        var hooks = document.RootElement.GetProperty("hooks");

        foreach (var name in new[] { "SessionStart", "SessionEnd" })
        {
            var command = hooks.GetProperty(name)[0]
                .GetProperty("hooks")[0]
                .GetProperty("command")
                .GetString();

            Assert.Contains("supervisor.exe", command!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(name == "SessionStart" ? "session-start" : "session-end", command!, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void IsIdempotentAcrossRepeatedRuns()
    {
        // Re-running install is how drift is repaired, so it has to be safe to run any number of
        // times. Appending a second copy of our block would double-fire every hook.
        var once = Load(HandMaintained).Install();
        var twice = Load(once).Install();
        var thrice = Load(twice).Install();

        Assert.Equal(once, twice);
        Assert.Equal(once, thrice);
    }

    [Fact]
    public void UninstallRestoresTheFileByteIdentically()
    {
        // The reason this unit is the most expensive to get wrong: a leftover hook fires forever
        // against a binary that may no longer exist. Byte-identical is the only bar worth setting —
        // "semantically equivalent" would still mean rewriting a file the developer maintains.
        var installed = Load(HandMaintained).Install();
        var restored = Load(installed).Uninstall();

        Assert.Equal(HandMaintained, restored);
    }

    [Fact]
    public void UninstallOnAFileWeNeverTouchedChangesNothing()
    {
        Assert.Equal(HandMaintained, Load(HandMaintained).Uninstall());
    }

    [Fact]
    public void DetectsWhetherWeAreInstalled()
    {
        Assert.False(Load(HandMaintained).IsInstalled);
        Assert.True(Load(Load(HandMaintained).Install()).IsInstalled);
    }

    [Fact]
    public void LeavesSomebodyElsesHooksAlone()
    {
        // A hooks key we did not create is the developer's, or another tool's. Uninstall must remove
        // our entries and leave theirs — removing the whole key would silently disable their tooling.
        const string withForeignHooks = """
            {
              "tui": "fullscreen",
              "hooks": {
                "SessionStart": [
                  {
                    "hooks": [
                      {
                        "type": "command",
                        "command": "someone-elses-tool --on-start"
                      }
                    ]
                  }
                ]
              }
            }
            """;

        var installed = Load(withForeignHooks).Install();
        Assert.Contains("someone-elses-tool", installed, StringComparison.Ordinal);

        var restored = Load(installed).Uninstall();

        Assert.Contains("someone-elses-tool", restored, StringComparison.Ordinal);
        Assert.DoesNotContain("supervisor.exe", restored, StringComparison.OrdinalIgnoreCase);

        using var document = System.Text.Json.JsonDocument.Parse(restored);
        Assert.Single(document.RootElement.GetProperty("hooks").GetProperty("SessionStart").EnumerateArray());
    }

    [Fact]
    public void AnEmptyOrAbsentFileBecomesAValidSettingsFile()
    {
        // `supervisor install` on a machine that has never run Claude Code interactively.
        var installed = SettingsFile.Parse("", Command).Install();

        using var document = System.Text.Json.JsonDocument.Parse(installed);
        Assert.True(document.RootElement.TryGetProperty("hooks", out _));
    }

    [Fact]
    public void RefusesToGuessAtAFileItCannotParse()
    {
        // Better to stop and say so than to overwrite a file whose contents we did not understand.
        // Everything in it is hand-written and unrecoverable if we get this wrong.
        Assert.Throws<InvalidSettingsException>(() => SettingsFile.Parse("{ not json", Command));
    }

    [Fact]
    public void PreservesCrlfLineEndings()
    {
        // Windows editors write CRLF, git may check out CRLF, and rewriting the file with LF would
        // show up as every line changed in any diff the developer runs.
        var crlf = HandMaintained.Replace("\n", "\r\n");

        var installed = SettingsFile.Parse(crlf, Command).Install();

        Assert.DoesNotContain("\n\n", installed, StringComparison.Ordinal);
        Assert.Contains("\r\n", installed, StringComparison.Ordinal);
        Assert.Equal(crlf, SettingsFile.Parse(installed, Command).Uninstall());
    }
}
