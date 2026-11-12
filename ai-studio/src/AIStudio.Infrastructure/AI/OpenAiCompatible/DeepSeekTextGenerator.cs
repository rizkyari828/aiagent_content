using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.AI.OpenAiCompatible;

public sealed class DeepSeekTextGenerator(
    HttpClient httpClient,
    IOptions<DeepSeekOptions> options,
    ILogger<DeepSeekTextGenerator> logger)
    : OpenAiCompatibleTextGenerator(
        httpClient,
        (options ?? throw new ArgumentNullException(nameof(options))).Value
            ?? throw new ArgumentException("Options value cannot be null.", nameof(options)),
        logger);
