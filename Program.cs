using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProjetoScrapping.Background;
using ProjetoScrapping.Services;

namespace ProjetoScrapping
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services
            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // CORS: permite tudo (para agora). Depois podemos travar por domínio.
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", policy =>
                    policy.AllowAnyOrigin()
                          .AllowAnyHeader()
                          .AllowAnyMethod());
            });

            // ======= ADIÇÃO IMPORTANTE =======
            // Cache + background + scraper
            builder.Services.AddMemoryCache();
            builder.Services.AddSingleton<INewsScraper, NewsScraper>();
            builder.Services.AddHostedService<NewsCacheRefresher>();
            // =================================

            var app = builder.Build();

            // Habilita Swagger quando estiver em Development OU quando a variável de ambiente ENABLE_SWAGGER= true
            var enableSwagger = app.Environment.IsDevelopment()
                                || string.Equals(System.Environment.GetEnvironmentVariable("ENABLE_SWAGGER"), "true", System.StringComparison.OrdinalIgnoreCase);

            if (enableSwagger)
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();
            app.UseCors("AllowAll");
            app.UseStaticFiles();
            app.UseAuthorization();

            // Endpoint de health-check
            app.MapGet("/health", () => Results.Ok("OK"));

            // Controllers (incluindo Notícias)
            app.MapControllers();

            // fallback para hospedar frontend no wwwroot
            app.MapFallbackToFile("index.html");

            app.Run();
        }
    }
}