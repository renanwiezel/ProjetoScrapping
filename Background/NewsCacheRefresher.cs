using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ProjetoScrapping.Models;
using ProjetoScrapping.Services;

namespace ProjetoScrapping.Background
{
    public class NewsCacheRefresher : BackgroundService
    {
        private readonly IMemoryCache _cache;
        private readonly INewsScraper _scraper;
        private readonly ILogger<NewsCacheRefresher> _log;

        // sites que você quer manter no cache
        private readonly string[] _sites = new[]
        {
        "https://noticias.uol.com.br",
        // adicione mais se quiser
    };

        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

        public NewsCacheRefresher(IMemoryCache cache, INewsScraper scraper, ILogger<NewsCacheRefresher> log)
        {
            _cache = cache; _scraper = scraper; _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                foreach (var site in _sites)
                {
                    try
                    {
                        var list = await _scraper.FetchAsync(site);
                        var payload = new CachedNews
                        {
                            Site = site,
                            Items = list,
                            UpdatedAt = DateTimeOffset.UtcNow
                        };

                        _cache.Set(CacheKey(site), payload, new MemoryCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                        });

                        _log.LogInformation("Cache atualizado: {site} ({count} itens)", site, list.Count);
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "Falha ao atualizar cache de {site}", site);
                    }
                }

                try { await Task.Delay(Interval, stoppingToken); }
                catch { /* cancelado */ }
            }
        }

        public static string CacheKey(string site) => $"news::{site}";
    }
}
