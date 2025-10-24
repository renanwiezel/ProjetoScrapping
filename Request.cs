using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace ProjetoScrapping
{
    public static class Request
    {
        private static readonly HttpClient client;
        private static readonly CookieContainer cookieJar = new CookieContainer();
        private static readonly Random rng = new Random();
        private static readonly HashSet<string> AllowedHosts;
        private static readonly string CookieStoreDir = Path.Combine(AppContext.BaseDirectory ?? ".", "Data");
        private static readonly string UolCookieFile = Path.Combine(CookieStoreDir, "uol_cookies.txt");
        private static readonly Uri UolRoot = new Uri("https://uol.com.br/");
        private static readonly Uri NoticiasRoot = new Uri("https://noticias.uol.com.br/");

        static Request()
        {
            var allowed = Environment.GetEnvironmentVariable("ALLOWED_SITES") ?? "https://noticias.uol.com.br/";
            AllowedHosts = allowed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                 .Select(urlOrHost =>
                                 {
                                     try
                                     {
                                         // Se for uma URL completa, extrai apenas o host
                                         if (urlOrHost.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                             urlOrHost.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                                         {
                                             return new Uri(urlOrHost).Host.ToLowerInvariant();
                                         }
                                         // Se já for apenas o host, usa direto
                                         return urlOrHost.ToLowerInvariant();
                                     }
                                     catch
                                     {
                                         return urlOrHost.ToLowerInvariant();
                                     }
                                 })
                                 .ToHashSet();

            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                UseCookies = true,
                CookieContainer = cookieJar,
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };

            // Remover configuração explícita de SSL - deixar o sistema decidir
            // handler.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;

            client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            // Usar HTTP/1.1 para melhor compatibilidade
            client.DefaultRequestVersion = new Version(1, 1);
            // client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;

            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("pt-BR,pt;q=0.9,en-US;q=0.8,en;q=0.7");
            client.DefaultRequestHeaders.Connection.ParseAdd("keep-alive");

            client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
            client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");

            try
            {
                var cookieString = Environment.GetEnvironmentVariable("UOL_COOKIES");
                if (!string.IsNullOrWhiteSpace(cookieString))
                {
                    EnsureCookieStoreDirectory();
                    File.WriteAllText(UolCookieFile, cookieString);
                    LoadCookiesFromString(UolRoot, cookieString);
                    LoadCookiesFromString(NoticiasRoot, cookieString);
                    Console.WriteLine("[INFO] Cookies UOL carregados de variável de ambiente");
                }
                else if (File.Exists(UolCookieFile))
                {
                    var stored = File.ReadAllText(UolCookieFile);
                    if (!string.IsNullOrWhiteSpace(stored))
                    {
                        LoadCookiesFromString(UolRoot, stored);
                        LoadCookiesFromString(NoticiasRoot, stored);
                        Console.WriteLine("[INFO] Cookies UOL carregados de arquivo");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Erro ao inicializar cookies UOL: {ex.Message}");
            }
        }

        private static void EnsureCookieStoreDirectory()
        {
            try { Directory.CreateDirectory(CookieStoreDir); } catch { }
        }

        private static void LoadCookiesFromString(Uri domain, string cookieString)
        {
            try
            {
                var cookies = cookieString.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var c in cookies)
                {
                    var parts = c.Split(new[] { '=' }, 2);
                    if (parts.Length == 2)
                    {
                        var name = parts[0].Trim();
                        var value = parts[1].Trim();
                        try { cookieJar.Add(domain, new Cookie(name, value)); }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private static void SaveCookiesToFile(Uri domain, string filePath)
        {
            try
            {
                EnsureCookieStoreDirectory();
                var cc = cookieJar.GetCookies(domain).Cast<Cookie>();
                var s = string.Join(';', cc.Select(c => c.Name + "=" + c.Value));
                File.WriteAllText(filePath, s);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Falha ao salvar cookies: {ex.Message}");
            }
        }

        private static async Task EnsureCookiesForUolAsync()
        {
            try
            {
                var existing = cookieJar.GetCookies(NoticiasRoot);
                if (existing != null && existing.Count > 0)
                {
                    Console.WriteLine($"[DEBUG] Usando {existing.Count} cookies existentes para notícias");
                    return;
                }

                Console.WriteLine("[INFO] Sem cookies. Favor configurar UOL_COOKIES manualmente.");
                Console.WriteLine("[INFO] No Chrome: F12 → Console → document.cookie");
                Console.WriteLine("[INFO] Depois: set UOL_COOKIES=\"cookie1=value1; cookie2=value2\"");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] Falha ao garantir cookies: {ex.Message}");
            }
        }

        public static bool IsHostAllowed(string url)
        {
            try
            {
                var host = new Uri(url).Host.ToLowerInvariant();
                foreach (var allowed in AllowedHosts)
                {
                    if (host == allowed) return true;
                    if (host.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase)) return true;
                    if (host.EndsWith(allowed, StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }
            catch { return false; }
        }

        public static string GetAllowedHosts() => string.Join(',', AllowedHosts);

        public static async Task<string> GetPageAsync(string url)
        {
            if (!IsHostAllowed(url))
                throw new HttpRequestException($"Host não permitido: {url}");

            try
            {
                var u = new Uri(url);
                if (u.Host.Contains("noticia.uol.com"))
                {
                    await EnsureCookiesForUolAsync();

                    var cookiesToSend = cookieJar.GetCookies(u);
                    Console.WriteLine($"[DEBUG] Enviando {cookiesToSend.Count} cookies para {u.Host}");
                }
            }
            catch { }

            var scraperKey = Environment.GetEnvironmentVariable("SCRAPER_API_KEY");
            if (!string.IsNullOrWhiteSpace(scraperKey))
            {
                Console.WriteLine("[DEBUG] Usando ScraperAPI");
                var proxy = $"http://api.scraperapi.com?api_key={WebUtility.UrlEncode(scraperKey)}&url={WebUtility.UrlEncode(url)}&render=true";
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, proxy);
                    using var res = await client.SendAsync(req);
                    res.EnsureSuccessStatusCode();
                    return await res.Content.ReadAsStringAsync();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"ScraperAPI falhou: {ex.Message}");
                }
            }

            int maxAttempts = 3;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    using var res = await client.SendAsync(req);

                    if (!res.IsSuccessStatusCode)
                    {
                        var body = await res.Content.ReadAsStringAsync();
                        throw new HttpRequestException($"GET {url} -> {res.StatusCode}");
                    }

                    return await res.Content.ReadAsStringAsync();
                }
                catch (HttpRequestException) when (attempt < maxAttempts)
                {
                    await Task.Delay(500 * attempt + rng.Next(0, 350));
                    continue;
                }
            }

            throw new HttpRequestException($"Falha ao obter '{url}' após {maxAttempts} tentativas");
        }

        public static List<Noticia> ParseHtml(string html, string baseUrl)
        {
            var noticias = new List<Noticia>();
            var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            try
            {
                var host = new Uri(baseUrl).Host.ToLowerInvariant();
                if (host.Contains("uol.com"))
                    TryPickForUol(doc, baseUrl, noticias, seenTitles, seenUrls);
            }
            catch { }

            if (noticias.Count < 10)
                TryPickBySelectors(doc, baseUrl, noticias, seenTitles, seenUrls);

            if (noticias.Count < 10)
                TryPickByMetaOg(doc, baseUrl, noticias, seenTitles, seenUrls);

            if (noticias.Count < 10)
                TryPickByJsonLd(doc, baseUrl, noticias, seenTitles, seenUrls);

            if (noticias.Count < 10)
                TryPickByAnchors(doc, baseUrl, noticias, seenTitles, seenUrls);

            return noticias.Take(30).ToList();
        }

        private static void TryPickForUol(HtmlDocument doc, string baseUrl,
            List<Noticia> outList, HashSet<string> seenTitles, HashSet<string> seenUrls)
        {
            var articles = doc.DocumentNode.SelectNodes("//article");
            if (articles != null)
            {
                foreach (var art in articles)
                {
                    var a = art.SelectSingleNode(".//a[@href]");
                    if (a == null) continue;

                    var href = a.GetAttributeValue("href", null);
                    if (string.IsNullOrWhiteSpace(href)) continue;

                    var titleNode = art.SelectSingleNode(".//h2|.//h3|.//h4");
                    var title = titleNode != null ? HtmlEntity.DeEntitize(titleNode.InnerText).Trim() : HtmlEntity.DeEntitize(a.InnerText).Trim();
                    if (string.IsNullOrWhiteSpace(title) || title.Length < 8) continue;

                    var url = ResolveUrl(baseUrl, href);
                    if (!IsLikelyNews(baseUrl, url)) continue;

                    if (seenTitles.Add(title) && seenUrls.Add(url))
                        outList.Add(new Noticia(title, null, url));

                    if (outList.Count >= 30) break;
                }
                if (outList.Count > 0) return;
            }

            var mainAnchors = doc.DocumentNode.SelectNodes("//main//a[@href] | //div[contains(@class,'home') or contains(@class,'section')]//a[@href]");
            if (mainAnchors != null)
            {
                foreach (var a in mainAnchors)
                {
                    var title = HtmlEntity.DeEntitize(a.InnerText).Trim();
                    if (string.IsNullOrWhiteSpace(title) || title.Length < 20) continue;

                    var href = a.GetAttributeValue("href", null);
                    if (string.IsNullOrWhiteSpace(href)) continue;

                    var url = ResolveUrl(baseUrl, href);
                    if (!IsLikelyNews(baseUrl, url)) continue;

                    if (seenTitles.Add(title) && seenUrls.Add(url))
                        outList.Add(new Noticia(title, null, url));

                    if (outList.Count >= 30) break;
                }
                if (outList.Count > 0) return;
            }

            var anchors = doc.DocumentNode.SelectNodes("//a[contains(@href,'/noticias/') or contains(@href,'/noticias.') or contains(@href,'/noticias-')]");
            if (anchors != null)
            {
                foreach (var a in anchors)
                {
                    var title = HtmlEntity.DeEntitize(a.InnerText).Trim();
                    if (string.IsNullOrWhiteSpace(title) || title.Length < 15) continue;

                    var href = a.GetAttributeValue("href", null);
                    if (string.IsNullOrWhiteSpace(href)) continue;

                    var url = ResolveUrl(baseUrl, href);
                    if (!IsLikelyNews(baseUrl, url)) continue;

                    if (seenTitles.Add(title) && seenUrls.Add(url))
                        outList.Add(new Noticia(title, null, url));

                    if (outList.Count >= 30) break;
                }
            }
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

                var a = h3.SelectSingleNode("./ancestor::a[1]") ?? h3.ParentNode?.SelectSingleNode(".//a[@href]");
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
            var scripts = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
            if (scripts == null) return;

            foreach (var s in scripts)
            {
                var json = s.InnerText;
                if (string.IsNullOrWhiteSpace(json)) continue;

                if (json.IndexOf("NewsArticle", StringComparison.OrdinalIgnoreCase) < 0 && json.IndexOf("\"Article\"", StringComparison.OrdinalIgnoreCase) < 0)
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

        private static string Clean(string? s) => HtmlEntity.DeEntitize(s ?? "").Trim();

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

            try
            {
                var baseHost = new Uri(baseUrl).Host;
                var urlHost = new Uri(url).Host;
                if (string.Equals(baseHost, urlHost, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }

            var u = url.ToLowerInvariant();
            return u.Contains("noticia")
                || u.Contains("noticias")
                || u.Contains("news")
                || u.Contains("feed")
                || u.Contains("rss")
                || u.Contains("uol")
                || u.Contains("g1")
                || u.Contains("folha");
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

        public static async Task<List<Noticia>> GetNoticiasAsync(string url)
        {
            var html = await GetPageAsync(url);
            return ParseHtml(html, url);
        }

        public static void UpdateUolCookies(string cookieString)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(cookieString)) return;
                EnsureCookieStoreDirectory();
                File.WriteAllText(UolCookieFile, cookieString);
                LoadCookiesFromString(UolRoot, cookieString);
                LoadCookiesFromString(NoticiasRoot, cookieString);
                Console.WriteLine("[INFO] Cookies UOL atualizados com sucesso!");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Falha ao atualizar cookies UOL: {ex.Message}");
            }
        }
    }
}
