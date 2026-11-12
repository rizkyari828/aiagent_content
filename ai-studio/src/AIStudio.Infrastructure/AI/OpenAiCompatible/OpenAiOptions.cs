namespace AIStudio.Infrastructure.AI.OpenAiCompatible;

public sealed class OpenAiOptions : OpenAiCompatibleOptions
{
    public new const string SectionName = "OpenAi";

    public OpenAiOptions()
    {
        BaseUrl = "https://api.openai.com/v1/";
        DefaultModel = "gpt-4o";
    }
}
