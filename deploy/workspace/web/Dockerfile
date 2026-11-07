# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0.402@sha256:747cbade5bfd918a175ac32f8f1d931edec2c7d0d5397a3de72eaef1ac1f66d2 AS build
WORKDIR /src
COPY . .
RUN dotnet restore RuntimeKnowledge.Web --locked-mode && \
    dotnet publish RuntimeKnowledge.Web -c Release --no-restore -o /out && \
    rm -f /out/wwwroot/appsettings.json.br /out/wwwroot/appsettings.json.gz

FROM caddy:2-alpine@sha256:78bd35025c7ba85961dd149ae670315129954e77abe2383ac47c89106b35ade9
COPY --from=build /out/wwwroot /srv
COPY <<'CADDYFILE' /etc/caddy/Caddyfile
:8080 {
    root * /srv
    header /appsettings.json Cache-Control no-store
    try_files {path} /index.html
    file_server {
        precompressed br gzip
    }
}
CADDYFILE
