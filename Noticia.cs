namespace ProjetoScrapping
{
    public class Noticia
    {
        public string Title { get; set; }
        public string? Description { get; set; }
        public string Url { get; set; }
        public DateTime? PublishedAt { get; set; }

        public Noticia(string title, string? description, string url, DateTime? publishedAt = null)
        {
            Title = title;
            Description = description;
            Url = url;
            PublishedAt = publishedAt;
        }
    }
}
