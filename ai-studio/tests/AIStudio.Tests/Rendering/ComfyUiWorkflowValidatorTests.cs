using AIStudio.Infrastructure.Rendering.ComfyUI;
using Xunit;

namespace AIStudio.Tests.Rendering;

/// <summary>
/// Pure trusted-JSON validation only: no workflow file is written to the repository and
/// no ComfyUI or model runtime is involved.
/// </summary>
public sealed class ComfyUiWorkflowValidatorTests
{
    [Fact]
    public void Validate_AcceptsTextOnlyWorkflow()
    {
        var result = ComfyUiWorkflowValidator.Validate(
            TextTemplate,
            ComfyUiWorkflowMode.TextOnly);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_AcceptsFirstFrameWorkflow()
    {
        var result = ComfyUiWorkflowValidator.Validate(
            FirstFrameTemplate,
            ComfyUiWorkflowMode.FirstFrame);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_AcceptsRealI2VWorkflow_TextOnly()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Rendering",
            "ComfyUI",
            "i2v_workflow.json");
        Assert.True(File.Exists(path), $"Workflow file '{path}' does not exist.");

        var json = File.ReadAllText(path);
        var result = ComfyUiWorkflowValidator.Validate(
            json,
            ComfyUiWorkflowMode.TextOnly);

        Assert.True(result.IsValid, result.Summary);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_AcceptsRealI2VWorkflow_FirstFrame()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Rendering",
            "ComfyUI",
            "i2v_workflow_first_frame.json");
        Assert.True(File.Exists(path), $"Workflow file '{path}' does not exist.");

        var json = File.ReadAllText(path);
        var result = ComfyUiWorkflowValidator.Validate(
            json,
            ComfyUiWorkflowMode.FirstFrame);

        Assert.True(result.IsValid, result.Summary);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_RejectsMissingPlaceholder()
    {
        var json = TextTemplate.Replace(
            ", \"seed\": \"__SEED__\"",
            string.Empty,
            StringComparison.Ordinal);

        var result = ComfyUiWorkflowValidator.Validate(json, ComfyUiWorkflowMode.TextOnly);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Code == ComfyUiWorkflowValidator.PlaceholderMissing
                && error.Message.Contains("__SEED__", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsPlaceholderInsideLargerText()
    {
        var json = TextTemplate.Replace(
            "\"__PROMPT__\"",
            "\"prefix __PROMPT__\"",
            StringComparison.Ordinal);

        var result = ComfyUiWorkflowValidator.Validate(json, ComfyUiWorkflowMode.TextOnly);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Code == ComfyUiWorkflowValidator.PlaceholderInvalid);
    }

    [Fact]
    public void Validate_RejectsUnknownToken()
    {
        var json = TextTemplate.Replace(
            "\"seed\": \"__SEED__\"",
            "\"seed\": \"__SEED__\", \"note\": \"__UNKNOWN_TOKEN__\"",
            StringComparison.Ordinal);

        var result = ComfyUiWorkflowValidator.Validate(json, ComfyUiWorkflowMode.TextOnly);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Code == ComfyUiWorkflowValidator.PlaceholderUnknown
                && error.Message.Contains("__UNKNOWN_TOKEN__", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("\"/home/tama/models/flux.safetensors\"")]
    [InlineData("\"C:\\\\models\\\\flux.safetensors\"")]
    [InlineData("\"https://example.com/flux.safetensors\"")]
    [InlineData("\"file:///home/tama/models/flux.safetensors\"")]
    [InlineData("\"bash -c 'rm -rf /'\"")]
    [InlineData("\"$(curl http://example.com/x)\"")]
    public void Validate_RejectsUnsafeStringValue(string jsonLiteral)
    {
        var json = TextTemplate.Replace(
            "\"seed\": \"__SEED__\"",
            $"\"seed\": \"__SEED__\", \"unsafe\": {jsonLiteral}",
            StringComparison.Ordinal);

        var result = ComfyUiWorkflowValidator.Validate(json, ComfyUiWorkflowMode.TextOnly);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Code == ComfyUiWorkflowValidator.ValueUnsafe);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"1\": ")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("")]
    public void Validate_RejectsMalformedOrNonObjectJson(string json)
    {
        var result = ComfyUiWorkflowValidator.Validate(json, ComfyUiWorkflowMode.TextOnly);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Code == ComfyUiWorkflowValidator.JsonInvalid);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"1\": \"not-a-node\"}")]
    [InlineData("{\"1\": {\"inputs\": {}}}")]
    [InlineData("{\"1\": {\"class_type\": \"\", \"inputs\": {}}}")]
    [InlineData("{\"1\": {\"class_type\": \"Node\", \"inputs\": []}}")]
    public void Validate_RejectsInvalidNodeShape(string json)
    {
        var result = ComfyUiWorkflowValidator.Validate(json, ComfyUiWorkflowMode.TextOnly);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Code == ComfyUiWorkflowValidator.NodeInvalid);
    }

    [Fact]
    public void Validate_RejectsReferenceImageInTextOnlyMode()
    {
        var result = ComfyUiWorkflowValidator.Validate(
            FirstFrameTemplate,
            ComfyUiWorkflowMode.TextOnly);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Code == ComfyUiWorkflowValidator.ModeMismatch
                && error.Message.Contains("__REFERENCE_IMAGE__", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RequiresReferenceImageInFirstFrameMode()
    {
        var result = ComfyUiWorkflowValidator.Validate(
            TextTemplate,
            ComfyUiWorkflowMode.FirstFrame);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Code == ComfyUiWorkflowValidator.ModeMismatch
                && error.Message.Contains("__REFERENCE_IMAGE__", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_IsDeterministic()
    {
        var json = TextTemplate
            .Replace("\"__PROMPT__\"", "\"/absolute/prompt\"", StringComparison.Ordinal)
            .Replace("\"__SEED__\"", "\"__MYSTERY__\"", StringComparison.Ordinal);

        var first = ComfyUiWorkflowValidator.Validate(json, ComfyUiWorkflowMode.TextOnly);
        var second = ComfyUiWorkflowValidator.Validate(json, ComfyUiWorkflowMode.TextOnly);

        Assert.Equal(first.Errors, second.Errors);
    }

    private const string TextTemplate = """
        {
          "1": {
            "class_type": "CLIPTextEncode",
            "inputs": { "text": "__PROMPT__", "seed": "__SEED__" }
          },
          "2": {
            "class_type": "EmptyLatentImage",
            "inputs": {
              "width": "__WIDTH__",
              "height": "__HEIGHT__",
              "fps": "__FPS__",
              "duration": "__DURATION_SECONDS__"
            }
          }
        }
        """;

    private const string FirstFrameTemplate = """
        {
          "1": {
            "class_type": "CLIPTextEncode",
            "inputs": { "text": "__PROMPT__", "seed": "__SEED__" }
          },
          "2": {
            "class_type": "EmptyLatentImage",
            "inputs": {
              "width": "__WIDTH__",
              "height": "__HEIGHT__",
              "fps": "__FPS__",
              "duration": "__DURATION_SECONDS__"
            }
          },
          "3": {
            "class_type": "LoadImage",
            "inputs": { "image": "__REFERENCE_IMAGE__" }
          }
        }
        """;
}
