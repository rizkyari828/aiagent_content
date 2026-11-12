using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.AI.OpenAiCompatible;

public sealed class OpenAiTextGenerator(
    HttpClient httpClient,
    IOptions<OpenAiOptions> options,
    ILogger<OpenAiTextGenerator> logger)
    : OpenAiCompatibleTextGenerator(
        httpClient,
        (options ?? throw new ArgumentNullException(nameof(options))).Value
            ?? throw new ArgumentException("Options value cannot be null.", nameof(options)),
        logger);
