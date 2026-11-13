namespace AIStudio.Infrastructure.Research;

public sealed class YouTubeOptions
{
    public const string SectionName = "YouTube";

    public bool Enabled { get; set; }

    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://www.googleapis.com/youtube/v3";

    public int TimeoutSeconds { get; set; } = 15;

    public int CommentMiningTopVideos { get; set; } = 3;

    public int CommentsPerVideo { get; set; } = 15;
}

public sealed class ResearchOptions
{
    public const string SectionName = "Research";

    public bool GoogleTrendsEnabled { get; set; } = true;

    public string GoogleTrendsBaseUrl { get; set; } = "https://trends.google.com/trending/rss";

    public int TimeoutSeconds { get; set; } = 15;

    public int CacheTtlHours { get; set; } = 8;

    public List<WatchlistFeedOption> WatchlistFeeds { get; set; } = [];
}

public sealed class WatchlistFeedOption
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string Category { get; set; } = "general";
}
