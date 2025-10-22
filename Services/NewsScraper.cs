using Microsoft.Extensions.Logging;

namespace ProjetoScrapping.Services
{


    public class NewsScraper : INewsScraper
    {
        private readonly ILogger<NewsScraper> _log;
        public NewsScraper(ILogger<NewsScraper> log) => _log = log;

        public async Task<List<Noticia>> FetchAsync(string site)
        {
            try
            {
                return await ProjetoScrapping.Request.GetNoticiasAsync(site);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Erro ao buscar notícias de {site}", site);
                return new List<Noticia>();
            }
        }
    }

}
