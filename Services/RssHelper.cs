using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel.Syndication;
using System.Xml;
using HtmlAgilityPack;
using ProjetoScrapping;

namespace ProjetoScrapping.Services
{
    public static class RssHelper
    {
        // Descobre links RSS na página inicial
        public static IEnumerable<string> DiscoverRssLinks(string html, string baseUrl)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var links = doc.DocumentNode.SelectNodes("//link[@rel='alternate' and (translate(@type,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')='application/rss+xml' or translate(@type,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')='application/atom+xml')]");
            if (links == null) yield break;

            foreach (var link in links)
            {
                var href = link.GetAttributeValue("href", null);
                if (string.IsNullOrWhiteSpace(href)) continue;
                yield return ResolveUrl(baseUrl, href);
            }
        }

        // Baixa e parseia um feed (RSS/Atom)
        public static List<Noticia> ParseFeed(string feedUrl)
        {
            var list = new List<Noticia>();
            if (string.IsNullOrWhiteSpace(feedUrl)) return list;

            try
            {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore };
                using var reader = XmlReader.Create(feedUrl, settings);
                var feed = SyndicationFeed.Load(reader);
                if (feed == null) return list;

                foreach (var item in feed.Items)
                {
                    var title = item.Title?.Text?.Trim();
                    if (string.IsNullOrWhiteSpace(title)) continue;

                    var link = item.Links?.FirstOrDefault()?.Uri?.ToString() ?? item.Id ?? string.Empty;
                    var desc = item.Summary?.Text;

                    DateTime? published = null;
                    if (item.PublishDate != DateTimeOffset.MinValue) published = item.PublishDate.UtcDateTime;
                    else if (item.LastUpdatedTime != DateTimeOffset.MinValue) published = item.LastUpdatedTime.UtcDateTime;

                    list.Add(new Noticia(title, desc, link, published));
                    if (list.Count >= 50) break;
                }
            }
            catch (Exception)
            {
                // falha ao baixar/parsear feed: retorna lista vazia
                // Em desenvolvimento, registre a exceção para depurar.
            }

            return list;
        }

        public static string ResolveUrl(string baseUrl, string href)
        {
            try { return new Uri(new Uri(baseUrl), href).ToString(); }
            catch { return href; }
        }
    }
}