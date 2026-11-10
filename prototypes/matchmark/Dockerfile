# The matchmark CLI as a container image (decision Q17), for teams without the .NET SDK. Build from the repository root:
#   docker build -t matchmark .
#   docker run --rm -v "$PWD:/work" matchmark audit openapi.yaml
#   docker run --rm -p 8080:8080 -v "$PWD:/work" matchmark host openapi.yaml --urls http://0.0.0.0:8080
# The runtime is ASP.NET Core's, since host and twin serve HTTP.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
# Behind a TLS-inspecting proxy, give its certificate as a build secret (--secret id=ca,src=proxy.crt) and the proxy as
# the usual HTTPS_PROXY build argument; without them, nothing changes.
RUN --mount=type=secret,id=ca,required=false \
    if [ -f /run/secrets/ca ]; then cp /run/secrets/ca /usr/local/share/ca-certificates/proxy.crt && update-ca-certificates; fi \
    && dotnet publish Matchmark.Cli/Matchmark.Cli.csproj --configuration Release --output /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
COPY --from=build /out /matchmark
WORKDIR /work
ENTRYPOINT ["dotnet", "/matchmark/matchmark.dll"]
