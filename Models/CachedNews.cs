namespace ProjetoScrapping.Models
{
    public class CachedNews
    {
        public string Site { get; set; } = "";
        public List<Noticia> Items { get; set; } = new();
        public DateTimeOffset UpdatedAt { get; set; }
    }

}
