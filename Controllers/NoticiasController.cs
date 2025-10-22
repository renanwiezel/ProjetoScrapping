using Microsoft.AspNetCore.Mvc;
using System.Text;
using System;
using System.Linq;

namespace ProjetoScrapping.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NoticiasController : ControllerBase
    {
        // GET api/noticias (mantém compatibilidade)
        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] string site = "https://noticias.uol.com.br")
        {
            var noticias = await ProjetoScrapping.Request.GetNoticiasAsync(site);

            // gera HTML simples com a lista de notícias
            var sb = new StringBuilder();
            sb.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>Noticias</title></head><body>");
            sb.AppendLine("<h1>Lista de notícias:</h1>");
            sb.AppendLine("<ul>");
            foreach (var n in noticias)
            {
                sb.AppendLine($"<li><a href=\"{System.Net.WebUtility.HtmlEncode(n.Url)}\" target=\"_blank\">{System.Net.WebUtility.HtmlEncode(n.Title)}</a></li>");
            }
            sb.AppendLine("</ul>");
            sb.AppendLine("</body></html>");

            return Content(sb.ToString(), "text/html; charset=utf-8");
        }

        // GET api/noticias/json -> retorna objeto { updatedAt, items }
        [HttpGet("json")]
        public async Task<IActionResult> GetJson([FromQuery] string site = "https://noticias.uol.com.br")
        {
            try
            {
                var noticias = await ProjetoScrapping.Request.GetNoticiasAsync(site);

                var result = new
                {
                    updatedAt = DateTime.UtcNow.ToString("o"),
                    items = noticias.Select(n => new
                    {
                        title = n.Title,
                        description = n.Description,
                        url = n.Url,
                        publishedAt = n.PublishedAt.HasValue ? n.PublishedAt.Value.ToString("o") : null
                    }).ToArray()
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                // Retorna detalhes para debugging (remova/alterar em produção)
                return Problem(detail: ex.ToString(), title: "Erro ao obter notícias");
            }
        }
    }
}