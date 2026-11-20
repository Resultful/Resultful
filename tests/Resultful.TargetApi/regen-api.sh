#!/usr/bin/env bash
# Regenerates one target project's PublicAPI.Shipped.txt files with the PublicAPI analyzers' own code fix (RS0016).
# Usage: tests/Resultful.TargetApi/regen-api.sh <Package>   (run from anywhere)
set -euo pipefail
cd "$(dirname "$0")/$1"
proj="$1.csproj"
for dir in PublicAPI/*/; do
  tfm=$(basename "$dir")
  printf '#nullable enable\n' > "$dir/PublicAPI.Shipped.txt"
  printf '#nullable enable\n' > "$dir/PublicAPI.Unshipped.txt"
  TargetApiTfm="$tfm" dotnet format analyzers "$proj" --diagnostics RS0016 --severity info -v q || true
  { printf '#nullable enable\n'; grep -v '^#nullable' "$dir/PublicAPI.Unshipped.txt" | LC_ALL=C sort; } > "$dir/PublicAPI.Shipped.txt"
  printf '#nullable enable\n' > "$dir/PublicAPI.Unshipped.txt"
done
