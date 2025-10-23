using Microsoft.AspNetCore.Mvc;

namespace ProjetoScrapping.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AdminController : ControllerBase
    {
        public class CookieUpdateModel
        {
            public string Cookies { get; set; } = string.Empty;
        }

        // POST api/admin/uol-cookies
        // Header required: X-Admin-Token with value equal to env var ADMIN_TOKEN
        [HttpPost("uol-cookies")]
        public IActionResult UpdateUolCookies([FromBody] CookieUpdateModel model)
        {
            var token = Request.Headers["X-Admin-Token"].FirstOrDefault();
            var expected = System.Environment.GetEnvironmentVariable("ADMIN_TOKEN");
            if (string.IsNullOrEmpty(expected) || token != expected)
            {
                return Unauthorized(new { error = "Unauthorized" });
            }

            if (model == null || string.IsNullOrWhiteSpace(model.Cookies))
            {
                return BadRequest(new { error = "Missing cookies" });
            }

            ProjetoScrapping.Request.UpdateUolCookies(model.Cookies);
            return Ok(new { status = "cookies updated" });
        }
    }
}
