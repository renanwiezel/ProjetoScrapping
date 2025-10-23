using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Security.Authentication;
using HtmlAgilityPack;

namespace ProjetoScrapping
{
    public static class Request
    {
        private static readonly HttpClient client;
        private static readonly CookieContainer cookieJar = new CookieContainer();
        private static readonly Random rng = new Random();

        static Request()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = true,
                UseCookies = true,
                CookieContainer = cookieJar,
            };

            handler.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;

            client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            client.DefaultRequestVersion = new Version(2, 0);
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;

            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Accept.ParseAdd(
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("pt-BR,pt;q=0.9,en-US;q=0.8,en;q=0.7");
            client.DefaultRequestHeaders.Connection.ParseAdd("keep-alive");

            client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
            client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
        }

        public static async Task<string> GetPageAsync(string url)
        {
            // Se existir variável de ambiente SCRAPER_API_KEY, usa proxy (ScraperAPI exemplo)
            var scraperKey = Environment.GetEnvironmentVariable("SCRAPER_API_KEY");
            if (!string.IsNullOrWhiteSpace(scraperKey))
            {
                // Exemplo com ScraperAPI; parâmetro render=true pede execução JS no serviço
                var proxy = $"http://api.scraperapi.com?api_key={WebUtility.UrlEncode(scraperKey)}&url={WebUtility.UrlEncode(url)}&render=true";
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, proxy);
                    using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                    if (!res.IsSuccessStatusCode)
                    {
                        var body = await SafeReadAsync(res);
                        throw new HttpRequestException($"Proxy request failed {res.StatusCode}. Snippet: {body}");
                    }
                    return await res.Content.ReadAsStringAsync();
                }
                catch (Exception ex)
                {
                    // se proxy falhar, tenta a rota normal abaixo
                    // (não rethrow para permitir fallback)
                    Console.Error.WriteLine($"Proxy fetch failed: {ex.Message}");
                }
            }

            // Fallback: tentativa direta (como antes)
            int maxAttempts = 3;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);

                    try
                    {
                        var baseUri = new Uri(url);
                        req.Headers.Referrer = new Uri(baseUri.GetLeftPart(UriPartial.Authority));
                    }
                    catch { }

                    using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);

                    if (!res.IsSuccessStatusCode)
                    {
                        var body = await SafeReadAsync(res);
                        var msg = $"GET {url} -> {(int)res.StatusCode} {res.ReasonPhrase}";
                        if (!string.IsNullOrEmpty(body))
                        {
                            var snippet = body.Length > 1200 ? body.Substring(0, 1200) : body;
                            msg += $"\nBody snippet:\n{snippet}";
                        }
                        throw new HttpRequestException(msg);
                    }

                    return await res.Content.ReadAsStringAsync();
                }
                catch (HttpRequestException) when (attempt < maxAttempts)
                {
                    await Task.Delay(500 * attempt + rng.Next(0, 350));
                    continue;
                }
            }

            throw new HttpRequestException($"Falha ao obter '{url}' após múltiplas tentativas.");
        }

        private static async Task<string> SafeReadAsync(HttpResponseMessage res)
        {
            try { return await res.Content.ReadAsStringAsync(); }
            catch { return ""; }
        }

        // ---------- PARSER ROBUSTO ----------
        public static List<Noticia> ParseHtml(string html, string baseUrl)
        {
            var noticias = new List<Noticia>();
            var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // 1) Alvos conhecidos (ajuste para o site real)
            TryPickBySelectors(doc, baseUrl, noticias, seenTitles, seenUrls);

            // 2) Fallback: metas (og:title / og:url)
            if (noticias.Count < 10)
                TryPickByMetaOg(doc, baseUrl, noticias, seenTitles, seenUrls);

            // 3) Fallback: JSON-LD (Article/NewsArticle)
            if (noticias.Count < 10)
                TryPickByJsonLd(doc, baseUrl, noticias, seenTitles, seenUrls);

            // 4) Fallback: varredura geral por <a> com título legível
            if (noticias.Count < 10)
                TryPickByAnchors(doc, baseUrl, noticias, seenTitles, seenUrls);

            return noticias.Take(30).ToList();
        }

        private static void TryPickBySelectors(HtmlDocument doc, string baseUrl,
            List<Noticia> outList, HashSet<string> seenTitles, HashSet<string> seenUrls)
        {
            var h3s = doc.DocumentNode.SelectNodes("//h3[contains(@class,'thumb-title')]");
            if (h3s == null) return;

            foreach (var h3 in h3s)
            {
                var title = Clean(h3.InnerText);
                if (string.IsNullOrWhiteSpace(title)) continue;

                var a = h3.SelectSingleNode("./ancestor::a[1]") ??
                        h3.ParentNode?.SelectSingleNode(".//a[@href]");

                if (a == null) continue;

                var href = a.GetAttributeValue("href", null);
                if (string.IsNullOrWhiteSpace(href)) continue;

                var url = ResolveUrl(baseUrl, href);
                if (!IsLikelyNews(baseUrl, url)) continue;

                if (seenTitles.Add(title) && seenUrls.Add(url))
                    outList.Add(new Noticia(title, null, url));
            }
        }

        private static void TryPickByMetaOg(HtmlDocument doc, string baseUrl,
            List<Noticia> outList, HashSet<string> seenTitles, HashSet<string> seenUrls)
        {
            var metas = doc.DocumentNode.SelectNodes("//meta[translate(@property,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')='og:title' or translate(@name,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')='og:title']");
            var urlMetas = doc.DocumentNode.SelectNodes("//meta[translate(@property,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')='og:url' or translate(@name,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')='og:url']");

            var title = metas?.FirstOrDefault()?.GetAttributeValue("content", null);
            var pageUrl = urlMetas?.FirstOrDefault()?.GetAttributeValue("content", null);

            if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(pageUrl))
            {
                pageUrl = ResolveUrl(baseUrl, pageUrl);
                if (IsLikelyNews(baseUrl, pageUrl) && seenTitles.Add(title) && seenUrls.Add(pageUrl))
                    outList.Add(new Noticia(title, null, pageUrl));
            }
        }

        private static void TryPickByJsonLd(HtmlDocument doc, string baseUrl,
            List<Noticia> outList, HashSet<string> seenTitles, HashSet<string> seenUrls)
        {
            // Procura <script type="application/ld+json"> com @type Article/NewsArticle
            var scripts = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
            if (scripts == null) return;

            foreach (var s in scripts)
            {
                var json = s.InnerText;
                if (string.IsNullOrWhiteSpace(json)) continue;

                // parsing leve só pra achar headline/url (evita dependência de JSON lib aqui)
                if (json.IndexOf("NewsArticle", StringComparison.OrdinalIgnoreCase) < 0 &&
                    json.IndexOf("\"Article\"", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var title = ExtractJsonField(json, "headline") ?? ExtractJsonField(json, "name");
                var url = ExtractJsonField(json, "url") ?? ExtractJsonField(json, "mainEntityOfPage");

                title = Clean(title);
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url)) continue;

                url = ResolveUrl(baseUrl, url);
                if (!IsLikelyNews(baseUrl, url)) continue;

                if (seenTitles.Add(title) && seenUrls.Add(url))
                    outList.Add(new Noticia(title, null, url));
            }
        }

        private static void TryPickByAnchors(HtmlDocument doc, string baseUrl,
            List<Noticia> outList, HashSet<string> seenTitles, HashSet<string> seenUrls)
        {
            var anchors = doc.DocumentNode.SelectNodes("//a[@href]");
            if (anchors == null) return;

            foreach (var a in anchors)
            {
                var title = Clean(a.InnerText);
                if (string.IsNullOrWhiteSpace(title) || title.Length < 15) continue;

                var href = a.GetAttributeValue("href", null);
                if (string.IsNullOrWhiteSpace(href)) continue;

                var url = ResolveUrl(baseUrl, href);
                if (!IsLikelyNews(baseUrl, url)) continue;

                if (seenTitles.Add(title) && seenUrls.Add(url))
                    outList.Add(new Noticia(title, null, url));
            }
        }

        // ---------- Helpers ----------
        private static string Clean(string? s) =>
            HtmlEntity.DeEntitize(s ?? "").Trim();

        private static string ResolveUrl(string baseUrl, string href)
        {
            try
            {
                var b = new Uri(baseUrl);
                var u = new Uri(b, href);
                return u.ToString();
            }
            catch { return href; }
        }

        private static bool IsLikelyNews(string baseUrl, string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            // se host bater com a host da base, aceitamos
            try
            {
                var baseHost = new Uri(baseUrl).Host;
                var urlHost = new Uri(url).Host;
                if (string.Equals(baseHost, urlHost, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { /* ignore */ }

            // fallback por palavras-chave (mantemos suporte a vários sites)
            var u = url.ToLowerInvariant();
            return u.Contains("noticia") || u.Contains("noticias") || u.Contains("news") || u.Contains("feed") || u.Contains("rss") || u.Contains("uol") || u.Contains("g1") || u.Contains("folha");
        }

        private static string? ExtractJsonField(string json, string field)
        {
            var key = $"\"{field}\"";
            var idx = json.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            idx = json.IndexOf(':', idx);
            if (idx < 0) return null;
            var rest = json.Substring(idx + 1).TrimStart();
            if (rest.StartsWith("\""))
            {
                rest = rest.Substring(1);
                var end = rest.IndexOf('"');
                if (end > 0) return rest.Substring(0, end);
            }
            return null;
        }

        // API pública
        public static async Task<List<Noticia>> GetNoticiasAsync(string url)
        {
            var html = await GetPageAsync(url);
            return ParseHtml(html, url);
        }
    }
}