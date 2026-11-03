using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIStudio.Infrastructure.Rendering.ComfyUI;

/// <summary>
/// Which trusted workflow contract a file must satisfy: the text-only graph or the
/// graph that consumes one uploaded first-frame reference.
/// </summary>
public enum ComfyUiWorkflowMode
{
    TextOnly = 0,
    FirstFrame = 1
}

/// <summary>One deterministic workflow validation problem.</summary>
public sealed record ComfyUiWorkflowValidationError(string Code, string Message);

/// <summary>
/// Deterministic validation result. It never mutates or repairs the workflow; the
/// caller either submits the original trusted JSON or rejects it.
/// </summary>
public sealed record ComfyUiWorkflowValidationResult(
    IReadOnlyList<ComfyUiWorkflowValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public string Summary => string.Join(
        " ",
        Errors.Select(error => $"[{error.Code}] {error.Message}"));
}

/// <summary>
/// Pure validator for a repository-owned ComfyUI workflow in API format. It answers
/// "is this trusted JSON safe to submit?" and deliberately makes no attempt to
/// understand or complete a graph:
/// <list type="bullet">
/// <item>the JSON must be valid and the root must be a non-empty object of nodes;</item>
/// <item>every node must have a non-empty string <c>class_type</c> and an object
/// <c>inputs</c>;</item>
/// <item>
/// every placeholder the provider substitutes must appear as a standalone JSON string
/// equal to its token, and no other <c>__...__</c> token is allowed;
/// </item>
/// <item>
/// string values may not carry an absolute path, a URL, or a shell command, because a
/// trusted graph only references installed models by name and never executes anything;
/// </item>
/// <item>
/// the text-only graph must not carry <c>__REFERENCE_IMAGE__</c> and the first-frame
/// graph must carry it.
/// </item>
/// </list>
/// The shell/path/URL checks are conservative heuristics documented to reject the
/// common unsafe shapes; they are not a sandbox.
/// </summary>
public static partial class ComfyUiWorkflowValidator
{
    public const string PromptToken = "__PROMPT__";
    public const string WidthToken = "__WIDTH__";
    public const string HeightToken = "__HEIGHT__";
    public const string FpsToken = "__FPS__";
    public const string DurationToken = "__DURATION_SECONDS__";
    public const string SeedToken = "__SEED__";
    public const string ReferenceImageToken = "__REFERENCE_IMAGE__";

    public const string JsonInvalid = "workflow_json_invalid";
    public const string NodeInvalid = "workflow_node_invalid";
    public const string PlaceholderMissing = "workflow_placeholder_missing";
    public const string PlaceholderInvalid = "workflow_placeholder_invalid";
    public const string PlaceholderUnknown = "workflow_placeholder_unknown";
    public const string ValueUnsafe = "workflow_value_unsafe";
    public const string ModeMismatch = "workflow_mode_mismatch";

    public static readonly IReadOnlyList<string> TextOnlyTokens =
    [
        PromptToken,
        WidthToken,
        HeightToken,
        FpsToken,
        DurationToken,
        SeedToken
    ];

    public static readonly IReadOnlyList<string> FirstFrameTokens =
    [
        .. TextOnlyTokens,
        ReferenceImageToken
    ];

    private static readonly HashSet<string> KnownTokens =
        new(FirstFrameTokens, StringComparer.Ordinal);

    public static ComfyUiWorkflowValidationResult Validate(
        string json,
        ComfyUiWorkflowMode mode)
    {
        var errors = new List<ComfyUiWorkflowValidationError>();

        if (string.IsNullOrWhiteSpace(json))
        {
            errors.Add(new(JsonInvalid, "The workflow JSON is empty."));
            return new ComfyUiWorkflowValidationResult(errors);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            errors.Add(new(
                JsonInvalid,
                $"The workflow JSON is malformed: {exception.Message}"));
            return new ComfyUiWorkflowValidationResult(errors);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new(JsonInvalid, "The workflow root must be a JSON object of nodes."));
                return new ComfyUiWorkflowValidationResult(errors);
            }

            var presentTokens = new HashSet<string>(StringComparer.Ordinal);
            var nodeCount = 0;

            foreach (var node in root.EnumerateObject())
            {
                nodeCount++;

                if (node.Value.ValueKind != JsonValueKind.Object)
                {
                    errors.Add(new(
                        NodeInvalid,
                        $"Node '{node.Name}' must be a JSON object."));
                    continue;
                }

                ValidateNode(node.Name, node.Value, errors);

                foreach (var value in EnumerateStrings(node.Value))
                {
                    CollectToken(value, presentTokens, errors);
                    ValidateValueSafety(value, errors);
                }
            }

            if (nodeCount == 0)
            {
                errors.Add(new(NodeInvalid, "The workflow must contain at least one node."));
            }

            var required = mode == ComfyUiWorkflowMode.FirstFrame
                ? FirstFrameTokens
                : TextOnlyTokens;

            foreach (var token in required)
            {
                if (!presentTokens.Contains(token))
                {
                    errors.Add(new(
                        PlaceholderMissing,
                        $"The workflow is missing the required placeholder '{token}'."));
                }
            }

            if (mode == ComfyUiWorkflowMode.TextOnly
                && presentTokens.Contains(ReferenceImageToken))
            {
                errors.Add(new(
                    ModeMismatch,
                    $"The text-only workflow must not contain '{ReferenceImageToken}'."));
            }

            if (mode == ComfyUiWorkflowMode.FirstFrame
                && !presentTokens.Contains(ReferenceImageToken))
            {
                errors.Add(new(
                    ModeMismatch,
                    $"The first-frame workflow must contain '{ReferenceImageToken}'."));
            }
        }

        return new ComfyUiWorkflowValidationResult(errors);
    }

    private static void ValidateNode(
        string name,
        JsonElement node,
        List<ComfyUiWorkflowValidationError> errors)
    {
        if (!node.TryGetProperty("class_type", out var classType)
            || classType.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(classType.GetString()))
        {
            errors.Add(new(
                NodeInvalid,
                $"Node '{name}' must have a non-empty string 'class_type'."));
        }

        if (!node.TryGetProperty("inputs", out var inputs)
            || inputs.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new(
                NodeInvalid,
                $"Node '{name}' must have an object 'inputs'."));
        }
    }

    private static IEnumerable<string> EnumerateStrings(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                yield return element.GetString()!;
                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    foreach (var value in EnumerateStrings(property.Value))
                    {
                        yield return value;
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var value in EnumerateStrings(item))
                    {
                        yield return value;
                    }
                }

                break;
        }
    }

    private static void CollectToken(
        string value,
        HashSet<string> presentTokens,
        List<ComfyUiWorkflowValidationError> errors)
    {
        if (KnownTokens.Contains(value))
        {
            presentTokens.Add(value);
            return;
        }

        foreach (Match match in TokenPattern().Matches(value))
        {
            var token = match.Value;
            if (!KnownTokens.Contains(token))
            {
                errors.Add(new(
                    PlaceholderUnknown,
                    $"The workflow contains the unknown placeholder '{token}'."));
            }
            else
            {
                errors.Add(new(
                    PlaceholderInvalid,
                    $"The placeholder '{token}' must appear as a standalone JSON string."));
            }
        }
    }

    private static void ValidateValueSafety(
        string value,
        List<ComfyUiWorkflowValidationError> errors)
    {
        if (KnownTokens.Contains(value))
        {
            return;
        }

        if (LooksLikeUrl(value))
        {
            errors.Add(new(
                ValueUnsafe,
                $"The workflow value '{value}' must not be a URL."));
            return;
        }

        if (LooksLikeAbsolutePath(value))
        {
            errors.Add(new(
                ValueUnsafe,
                $"The workflow value '{value}' must not be an absolute path."));
            return;
        }

        if (LooksLikeShellCommand(value))
        {
            errors.Add(new(
                ValueUnsafe,
                $"The workflow value '{value}' must not contain a shell command."));
        }
    }

    private static bool LooksLikeUrl(string value) =>
        value.StartsWith("//", StringComparison.Ordinal)
        || value.Contains("file:", StringComparison.OrdinalIgnoreCase)
        || (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
                || string.Equals(uri.Scheme, Uri.UriSchemeFtp, StringComparison.Ordinal)
                || string.Equals(uri.Scheme, "ws", StringComparison.Ordinal)
                || string.Equals(uri.Scheme, "wss", StringComparison.Ordinal)));

    private static bool LooksLikeAbsolutePath(string value) =>
        value.StartsWith('/')
        || value.StartsWith("\\\\", StringComparison.Ordinal)
        || WindowsDrivePath().IsMatch(value)
        || value.StartsWith("~/", StringComparison.Ordinal)
        || value.StartsWith("~\\", StringComparison.Ordinal);

    private static bool LooksLikeShellCommand(string value) =>
        value.Contains('`')
        || value.Contains("$(", StringComparison.Ordinal)
        || value.Contains("&&", StringComparison.Ordinal)
        || value.Contains("||", StringComparison.Ordinal)
        || ShellInterpreter().IsMatch(value)
        || ShellProgram().IsMatch(value);

    [GeneratedRegex(@"__[A-Za-z0-9_]+__", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"^[A-Za-z]:[\\/]", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsDrivePath();

    [GeneratedRegex(
        @"(?i)\b(?:bash|sh|zsh|powershell|pwsh|cmd(?:\.exe)?)\s+-[A-Za-z]",
        RegexOptions.CultureInvariant)]
    private static partial Regex ShellInterpreter();

    [GeneratedRegex(
        @"(?i)(?:^|[;&|]\s*)(?:sudo|rm|del|rmdir|curl|wget)\s",
        RegexOptions.CultureInvariant)]
    private static partial Regex ShellProgram();
}
