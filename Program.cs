using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using ProjetoScrapping.Background;
using ProjetoScrapping.Services;

var builder = WebApplication.CreateBuilder(args);

// services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// caching + scraper + background refresher
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<INewsScraper, NewsScraper>();
builder.Services.AddHostedService<NewsCacheRefresher>();

// CORS (em prod: restrinja aos domínios do seu front)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

// aceitar X-Forwarded-* do proxy do Render
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// Swagger em dev ou ENABLE_SWAGGER=true
var enableSwagger = app.Environment.IsDevelopment()
    || string.Equals(Environment.GetEnvironmentVariable("ENABLE_SWAGGER"), "true", StringComparison.OrdinalIgnoreCase);
if (enableSwagger)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");

// wwwroot (com cache opcional)
app.UseStaticFiles(/* ver opção de cache acima */);

app.UseAuthorization();

// health
app.MapGet("/health", () => Results.Ok("OK"));

// controllers
app.MapControllers();

// SPA fallback
app.MapFallbackToFile("index.html");

app.Run();