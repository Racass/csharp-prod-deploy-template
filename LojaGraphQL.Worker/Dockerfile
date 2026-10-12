FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish LojaGraphQL.Worker/LojaGraphQL.Worker.csproj -c Release -o /publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /publish .
USER app
ENTRYPOINT ["dotnet", "LojaGraphQL.Worker.dll"]
