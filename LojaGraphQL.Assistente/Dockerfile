FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY LojaGraphQL.Assistente/LojaGraphQL.Assistente.csproj LojaGraphQL.Assistente/
RUN dotnet restore LojaGraphQL.Assistente/LojaGraphQL.Assistente.csproj
COPY LojaGraphQL.Assistente/ LojaGraphQL.Assistente/
RUN dotnet publish LojaGraphQL.Assistente/LojaGraphQL.Assistente.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "LojaGraphQL.Assistente.dll"]
