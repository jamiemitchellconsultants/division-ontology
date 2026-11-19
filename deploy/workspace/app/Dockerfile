FROM mcr.microsoft.com/dotnet/sdk:10.0.402@sha256:747cbade5bfd918a175ac32f8f1d931edec2c7d0d5397a3de72eaef1ac1f66d2 AS build
WORKDIR /src
COPY . .
RUN dotnet restore RuntimeKnowledge.Api --locked-mode && dotnet restore RuntimeKnowledge.Worker --locked-mode
RUN dotnet publish RuntimeKnowledge.Api -c Release --no-restore -p:OpenApiGenerateDocuments=false -o /out/api && \
    dotnet publish RuntimeKnowledge.Worker -c Release --no-restore -o /out/worker

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13@sha256:8af87eba5e7cdafb1514d7388be287365da97c132509e84dfe252ffd552398ca AS base
WORKDIR /app
USER app

FROM base AS worker
COPY --from=build /out/worker .
ENTRYPOINT ["dotnet", "RuntimeKnowledge.Worker.dll"]

FROM base AS api
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
USER app
COPY --from=build /out/api .
EXPOSE 8080
ENTRYPOINT ["dotnet", "RuntimeKnowledge.Api.dll"]
