# The matchmark CLI as a container image (decision Q17), for teams without the .NET SDK. Build from the repository root:
#   docker build -f prototypes/matchmark/Dockerfile -t matchmark .
#   docker run --rm -v "$PWD:/work" matchmark audit openapi.yaml
#   docker run --rm -p 8080:8080 -v "$PWD:/work" matchmark host openapi.yaml --urls http://0.0.0.0:8080
# The runtime is ASP.NET Core's, since host and twin serve HTTP.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY Resultful/ Resultful/
COPY prototypes/matchmark/ prototypes/matchmark/
RUN dotnet publish prototypes/matchmark/Matchmark.Cli/Matchmark.Cli.csproj --configuration Release --output /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
COPY --from=build /out /matchmark
WORKDIR /work
ENTRYPOINT ["dotnet", "/matchmark/matchmark.dll"]
