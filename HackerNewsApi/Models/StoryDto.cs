namespace HackerNewsApi.Models
{
    public class StoryDto
    {
        public string Title { get; set; } = string.Empty;
        public string? Uri { get; set; }
        public string? PostedBy { get; set; }
        public DateTimeOffset Time { get; set; }
        public int Score { get; set; }
        public int CommentCount { get; set; }
    }
}
