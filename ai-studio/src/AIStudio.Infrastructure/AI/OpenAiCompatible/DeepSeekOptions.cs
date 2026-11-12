namespace AIStudio.Infrastructure.AI.OpenAiCompatible;

public sealed class DeepSeekOptions : OpenAiCompatibleOptions
{
    public new const string SectionName = "DeepSeek";

    public DeepSeekOptions()
    {
        BaseUrl = "https://api.deepseek.com/v1/";
        DefaultModel = "deepseek-chat";
    }
}
