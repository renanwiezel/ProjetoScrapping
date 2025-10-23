using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ProjetoScrapping.Services
{
    public class NewsScraper : INewsScraper
    {
        private readonly ILogger<NewsScraper> _log;

        public NewsScraper(ILogger<NewsScraper> log) => _log = log;

        public async Task<List<Noticia>> FetchAsync(string site)
        {
            // 1) Tentar descobrir e usar feeds (RSS/Atom) primeiro — grátis e mais estável
            try
            {
                _log.LogDebug("Tentando descobrir feeds RSS/Atom na home: {site}", site);
                var homeHtml = await ProjetoScrapping.Request.GetPageAsync(site);
                var rssLinks = RssHelper
                    .DiscoverRssLinks(homeHtml, site)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (rssLinks.Count > 0)
                {
                    _log.LogInformation("Feeds encontrados: {count} para {site}", rssLinks.Count, site);
                    foreach (var feedUrl in rssLinks)
                    {
                        try
                        {
                            var feedItems = RssHelper.ParseFeed(feedUrl);
                            if (feedItems != null && feedItems.Count > 0)
                            {
                                _log.LogInformation("Usando feed {feed} ({count} itens) para {site}.", feedUrl, feedItems.Count, site);
                                return feedItems;
                            }
                            _log.LogWarning("Feed {feed} não retornou itens.", feedUrl);
                        }
                        catch (Exception ex)
                        {
                            _log.LogWarning(ex, "Falha ao ler feed {feedUrl}", feedUrl);
                        }
                    }
                }
                else
                {
                    _log.LogWarning("Nenhum feed descoberto automaticamente em {site}", site);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Erro ao descobrir feeds na home {site}", site);
            }

            // 2) Se não tivermos feeds úteis, tentar HTML scraping (fallback)
            try
            {
                _log.LogInformation("Tentando HTML scraping como fallback para {site}", site);
                var htmlList = await ProjetoScrapping.Request.GetNoticiasAsync(site);
                if (htmlList != null && htmlList.Count > 0)
                {
                    _log.LogInformation("HTML scraping OK ({count} itens) para {site}", htmlList.Count, site);
                    return htmlList;
                }
                _log.LogWarning("HTML scraping retornou vazio para {site}. Partindo para fallbacks manuais…", site);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Falha no HTML scraping de {site}. Tentando fallbacks…", site);
            }

            // 3) Fallbacks manuais (feeds conhecidos)
            var fallbacks = new[]
            {
                // referências ao UOL comentadas para manter apenas sbravattimarcas nos testes
                "https://feeds.folha.uol.com.br/emcimadahora/rss091.xml",
                "https://g1.globo.com/rss/g1/",
                "https://www.estadao.com.br/rss/ultimas.xml",
                "https://www.uol.com.br/feed.xml"
            };

            foreach (var f in fallbacks)
            {
                try
                {
                    var feedItems = RssHelper.ParseFeed(f);
                    if (feedItems != null && feedItems.Count > 0)
                    {
                        _log.LogInformation("Usando feed fallback {feed} ({count} itens).", f, feedItems.Count);
                        return feedItems;
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Falha ao ler fallback feed {feed}", f);
                }
            }

            // 4) Nada funcionou — devolve vazio
            _log.LogWarning($"Não foi possível obter notícias para {site} via RSS nem HTML.", site);
            return new List<Noticia>();
        }
    }
}