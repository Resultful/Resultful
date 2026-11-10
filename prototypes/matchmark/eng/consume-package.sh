#!/usr/bin/env bash
# The consumption test: pack Matchmark.Generator, then build a fresh project, outside this repository's build files,
# that references only the package and marks the Pet Store spec. Warnings are errors, and Program calls a generated
# Map method, so the package has to deliver a generator that runs, its dependencies, and the props that make the
# metadata visible. Usage: eng/consume-package.sh [work directory]
set -euo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
work="${1:-$(mktemp -d)}"
rm -rf "$work/feed" "$work/consumer"
mkdir -p "$work/feed" "$work/consumer"

dotnet pack "$here/Matchmark.Generator" -c Release -o "$work/feed" -p:PackageVersion=0.0.0-local --nologo -v quiet

cd "$work/consumer"
cp "$here/Matchmark.Sample.Contract/petstore.yaml" .
# An overlay, so Matchmark's own readers (Matchmark.Specs, Matchmark.Specs.Overlay) are loaded from the package too.
cat > petstore.overlay.yaml <<'YAML'
overlay: 1.1.0
info: { title: Consumed, version: 1.0.0 }
actions:
  - target: $.components.schemas.Pet.properties.name
    update: { maxLength: 77 }
YAML
cat > nuget.config <<XML
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$work/feed" />
    <add key="nuget" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
XML
cat > Consumer.csproj <<'XML'
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Matchmark.Generator" Version="0.0.0-local" PrivateAssets="all" />
    <AdditionalFiles Include="petstore.yaml" Matchmark="true" MatchmarkParts="Contract,Server" />
    <AdditionalFiles Include="petstore.overlay.yaml" MatchmarkOverlay="true" MatchmarkApiName="Petstore" />
  </ItemGroup>
</Project>
XML
cat > Program.cs <<'CS'
using Consumer;

var app = WebApplication.CreateBuilder(args).Build();
app.MapPetstore();
CS
dotnet build -c Release --nologo

# What was examined: the generator ran and wrote the parts asked for.
generated="$(find obj -path '*Matchmark.Generator.OpenApiSourceGenerator*' -name '*.g.cs' | wc -l)"
echo "generated sources: $generated"
test "$generated" -ge 5
# And the overlay was read and applied by the readers in the package: the name's maxLength is the overlay's.
grep -rq '77' obj/Release/net10.0/generated/Matchmark.Generator/Matchmark.Generator.OpenApiSourceGenerator/Petstore.Models.g.cs
echo "overlay applied"
