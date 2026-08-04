using System.Text.Json;
using System.Text.Json.Nodes;

namespace Supervisor.Core.Install;

/// <summary>Raised when the settings file cannot be understood, so nothing is written.</summary>
/// <remarks>
/// Refusing is the safe outcome. Everything in that file is hand-written and unrecoverable, so
/// guessing at a malformed one risks destroying work in exchange for saving the developer a
/// sentence of explanation.
/// </remarks>
public sealed class InvalidSettingsException : Exception
{
    public InvalidSettingsException(string message) : base(message)
    {
    }
}

/// <summary>
/// Adds and removes TheSupervisor's lifecycle hooks in Claude Code's <c>settings.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This edits a file the developer maintains by hand.</strong> Round-tripping it through a
/// JSON serializer would be semantically identical and still wrong: it reorders nothing but
/// reformats everything, so the next diff shows every line changed and the developer's own
/// formatting is gone. So the edit is textual — a splice at one span — and everything outside that
/// span survives byte for byte.
/// </para>
/// <para>
/// The marker is the command string: an entry is ours when its command names our executable. JSON
/// has no comments, so there is nowhere to put a literal marker, and inventing an extra field
/// risks a schema that rejects unknown keys.
/// </para>
/// </remarks>
public sealed class SettingsFile
{
    private const string HooksProperty = "hooks";

    /// <summary>The events we register. Session lifecycle only — never per-tool-call (ADR-0005).</summary>
    private static readonly (string Event, string Verb)[] _events =
    [
        ("SessionStart", "session-start"),
        ("SessionEnd", "session-end"),
    ];

    private readonly string _text;
    private readonly string _command;
    private readonly string _newline;
    private readonly string _indent;

    private SettingsFile(string text, string command, string newline, string indent)
    {
        _text = text;
        _command = command;
        _newline = newline;
        _indent = indent;
    }

    /// <summary>
    /// Reads a settings file. An empty file is a new one; an unparseable file is refused.
    /// </summary>
    /// <param name="command">
    /// How to invoke this tool, already quoted if the path needs it — the hook command is this plus
    /// the verb.
    /// </param>
    public static SettingsFile Parse(string? text, string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        var content = string.IsNullOrWhiteSpace(text) ? "{}" : text;

        try
        {
            using var document = JsonDocument.Parse(content);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidSettingsException("settings.json is not a JSON object.");
            }
        }
        catch (JsonException e)
        {
            throw new InvalidSettingsException($"settings.json is not valid JSON: {e.Message}");
        }

        // CRLF is what Windows editors write. Rewriting with LF would show up as every line changed
        // in any diff the developer runs.
        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        return new SettingsFile(content, command, newline, DetectIndent(content));
    }

    /// <summary>Whether our hooks are already present.</summary>
    public bool IsInstalled => Hooks() is { } hooks && OurEntryCount(hooks) > 0;

    /// <summary>Returns the file with our hooks present. Safe to run repeatedly.</summary>
    public string Install()
    {
        var hooks = Hooks() ?? new JsonObject();

        // Drop ours first, then add them back. Re-running install is how drift is repaired, and
        // appending without this would double-fire every hook on the second run.
        RemoveOurEntries(hooks);

        foreach (var (name, verb) in _events)
        {
            if (hooks[name] is not JsonArray matchers)
            {
                matchers = [];
                hooks[name] = matchers;
            }

            matchers.Add(new JsonObject
            {
                ["hooks"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "command",
                        ["command"] = $"{_command} hook {verb}",
                    },
                },
            });
        }

        return Splice(hooks);
    }

    /// <summary>
    /// Returns the file with our hooks removed, and nothing else changed.
    /// </summary>
    /// <remarks>
    /// When we added the whole <c>hooks</c> key, this restores the file <em>byte for byte</em>. That
    /// is the bar this unit is held to: a leftover hook fires forever against a binary that may no
    /// longer exist, and "semantically equivalent" would still mean rewriting a hand-maintained file.
    /// </remarks>
    public string Uninstall()
    {
        if (Hooks() is not { } hooks)
        {
            return _text;
        }

        if (OurEntryCount(hooks) == 0)
        {
            return _text;
        }

        RemoveOurEntries(hooks);

        return Splice(hooks);
    }

    private JsonObject? Hooks()
    {
        using var document = JsonDocument.Parse(_text);

        return document.RootElement.TryGetProperty(HooksProperty, out var hooks)
            && hooks.ValueKind == JsonValueKind.Object
                ? JsonNode.Parse(hooks.GetRawText())!.AsObject()
                : null;
    }

    private bool IsOurs(JsonNode? matcher) =>
        matcher is JsonObject group
        && group["hooks"] is JsonArray entries
        && entries.Any(e =>
            e?["command"]?.GetValue<string>() is { } command
            && command.Contains(_command, StringComparison.OrdinalIgnoreCase));

    private int OurEntryCount(JsonObject hooks) =>
        hooks.Sum(pair => pair.Value is JsonArray matchers ? matchers.Count(IsOurs) : 0);

    /// <summary>
    /// Strips our entries, and any event array they leave empty.
    /// </summary>
    /// <remarks>
    /// An event array we emptied was one we created, so leaving <c>"SessionStart": []</c> behind
    /// would be a trace of us in a file we promised to leave clean. Another tool's entries in the
    /// same array are untouched — removing the array wholesale would silently disable their tooling.
    /// </remarks>
    private void RemoveOurEntries(JsonObject hooks)
    {
        foreach (var name in hooks.Select(p => p.Key).ToList())
        {
            if (hooks[name] is not JsonArray matchers)
            {
                continue;
            }

            for (var i = matchers.Count - 1; i >= 0; i--)
            {
                if (IsOurs(matchers[i]))
                {
                    matchers.RemoveAt(i);
                }
            }

            if (matchers.Count == 0)
            {
                hooks.Remove(name);
            }
        }
    }

    /// <summary>
    /// Replaces (or removes, or inserts) the <c>hooks</c> property, touching no other byte.
    /// </summary>
    private string Splice(JsonObject hooks)
    {
        var existing = FindTopLevelProperty(_text, HooksProperty);

        if (hooks.Count == 0)
        {
            if (existing is not ({ } start, { } end))
            {
                return _text;
            }

            // Take the separator with it. Leaving the comma behind produces invalid JSON; leaving
            // the whitespace behind leaves a blank line where we used to be, which is not the same
            // file we were handed.
            var from = BackOverSeparator(_text, start, out var removedComma);
            var to = removedComma ? end : SkipTrailingComma(_text, end);

            return _text[..from] + _text[to..];
        }

        var rendered = Render(hooks);

        if (existing is ({ } replaceStart, { } replaceEnd))
        {
            return _text[..replaceStart] + rendered + _text[replaceEnd..];
        }

        // No hooks key: insert before the root's closing brace, after whatever is already there.
        var close = _text.LastIndexOf('}');
        var lastValue = LastNonWhitespace(_text, close);
        var separator = _text[lastValue] == '{' ? "" : ",";

        return _text[..(lastValue + 1)]
            + separator + _newline + _indent + rendered
            + _newline + _text[(lastValue + 1)..].TrimStart('\r', '\n', ' ', '\t');
    }

    /// <summary>Renders the property as text, indented to sit one level inside the root object.</summary>
    private string Render(JsonObject hooks)
    {
        var json = hooks.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            IndentCharacter = _indent.Length > 0 && _indent[0] == '\t' ? '\t' : ' ',
            IndentSize = _indent.Length > 0 && _indent[0] == '\t' ? 1 : _indent.Length,
        });

        var lines = json.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

        // Every line after the first sits one level deeper than the property itself.
        for (var i = 1; i < lines.Length; i++)
        {
            lines[i] = _indent + lines[i];
        }

        return $"\"{HooksProperty}\": " + string.Join(_newline, lines);
    }

    /// <summary>The indent the file already uses, so our block matches its neighbours.</summary>
    private static string DetectIndent(string text)
    {
        foreach (var raw in text.Split('\n').Skip(1))
        {
            var line = raw.TrimEnd('\r');
            var leading = line.Length - line.TrimStart(' ', '\t').Length;

            if (leading > 0 && line.Trim().Length > 0)
            {
                return line[..leading];
            }
        }

        return "  ";
    }

    /// <summary>
    /// Locates a top-level property, from the opening quote of its name to the end of its value.
    /// </summary>
    private static (int Start, int End)? FindTopLevelProperty(string text, string name)
    {
        var i = SkipWhitespace(text, 0);

        if (i >= text.Length || text[i] != '{')
        {
            return null;
        }

        i = SkipWhitespace(text, i + 1);

        while (i < text.Length && text[i] == '"')
        {
            var nameStart = i;
            var nameEnd = SkipString(text, i);
            var found = text[(nameStart + 1)..(nameEnd - 1)] == name;

            i = SkipWhitespace(text, nameEnd);
            if (i >= text.Length || text[i] != ':')
            {
                return null;
            }

            i = SkipWhitespace(text, i + 1);
            var valueEnd = SkipValue(text, i);

            if (found)
            {
                return (nameStart, valueEnd);
            }

            i = SkipWhitespace(text, valueEnd);
            if (i < text.Length && text[i] == ',')
            {
                i = SkipWhitespace(text, i + 1);
            }
        }

        return null;
    }

    private static int BackOverSeparator(string text, int start, out bool removedComma)
    {
        var i = start - 1;
        while (i >= 0 && char.IsWhiteSpace(text[i]))
        {
            i--;
        }

        removedComma = i >= 0 && text[i] == ',';
        return removedComma ? i : start;
    }

    private static int SkipTrailingComma(string text, int end)
    {
        var i = SkipWhitespace(text, end);
        return i < text.Length && text[i] == ',' ? i + 1 : end;
    }

    private static int LastNonWhitespace(string text, int before)
    {
        var i = before - 1;
        while (i >= 0 && char.IsWhiteSpace(text[i]))
        {
            i--;
        }

        return i;
    }

    private static int SkipWhitespace(string text, int i)
    {
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        return i;
    }

    /// <summary>Index just past the closing quote of the string starting at <paramref name="i"/>.</summary>
    private static int SkipString(string text, int i)
    {
        i++;

        while (i < text.Length)
        {
            if (text[i] == '\\')
            {
                i += 2;
                continue;
            }

            if (text[i] == '"')
            {
                return i + 1;
            }

            i++;
        }

        return i;
    }

    /// <summary>Index just past the value starting at <paramref name="i"/>.</summary>
    private static int SkipValue(string text, int i)
    {
        if (i >= text.Length)
        {
            return i;
        }

        switch (text[i])
        {
            case '"':
                return SkipString(text, i);

            case '{':
            case '[':
                var open = text[i];
                var close = open == '{' ? '}' : ']';
                var depth = 0;

                while (i < text.Length)
                {
                    if (text[i] == '"')
                    {
                        i = SkipString(text, i);
                        continue;
                    }

                    if (text[i] == open)
                    {
                        depth++;
                    }
                    else if (text[i] == close)
                    {
                        depth--;
                        if (depth == 0)
                        {
                            return i + 1;
                        }
                    }

                    i++;
                }

                return i;

            default:
                while (i < text.Length && text[i] is not (',' or '}' or ']') && !char.IsWhiteSpace(text[i]))
                {
                    i++;
                }

                return i;
        }
    }
}
