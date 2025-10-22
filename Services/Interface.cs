namespace ProjetoScrapping.Services
{
    public interface INewsScraper
    {
        Task<List<Noticia>> FetchAsync(string site);
    }
}
