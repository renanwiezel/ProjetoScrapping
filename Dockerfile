# build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -o /app

# runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app .

# O Render injeta a variável PORT em runtime.
# Usamos 'sh -c' para expandir ${PORT} em tempo de execução.
EXPOSE 10000
CMD ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT} dotnet ProjetoScrapping.dll"]
