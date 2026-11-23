FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY WorksArchive.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Render (і більшість хостингів) передає порт у змінній PORT; локально — 8080.
ENV PORT=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0
EXPOSE 8080
CMD ["sh", "-c", "ASPNETCORE_HTTP_PORTS=$PORT exec dotnet WorksArchive.dll"]
