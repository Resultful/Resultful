#!/usr/bin/env bash
# Pack-and-inspect check for the Resultful nupkg.
#
# Usage: eng/verify-package.sh <path-to-Resultful.x.y.z.nupkg>
#
# Fails if the package regresses to the 0.2.0-beta05 shape that broke Paket/NuGet audit consumers:
#   - a NETStandard.Library or System.ValueTuple dependency
#   - a .NETStandard1.x or .NETFramework dependency group (beta05 had a bogus ".NETFramework5.0")
#   - a OneOf floor below 3.0.271 (3.0.178 pulls NETStandard.Library 1.6.1 -> NU1903 on net8+)
# and checks the metadata and symbols package are present.
set -euo pipefail

nupkg="${1:?usage: $0 <path-to-nupkg>}"
[[ -f "$nupkg" ]] || { echo "error: $nupkg not found" >&2; exit 2; }

min_oneof="3.0.271"
failures=()
fail() { failures+=("$1"); }

nuspec="$(unzip -p "$nupkg" '*.nuspec')"
files="$(unzip -Z1 "$nupkg")"

# Dependencies that must never come back.
for banned in NETStandard.Library System.ValueTuple; do
    if grep -q "id=\"$banned\"" <<<"$nuspec"; then
        fail "nuspec depends on $banned"
    fi
done

# Dependency groups.
groups="$(grep -o 'group targetFramework="[^"]*"' <<<"$nuspec" | sed 's/.*="\(.*\)"/\1/' || true)"
[[ -n "$groups" ]] || fail "nuspec has no dependency groups"
while IFS= read -r g; do
    [[ -z "$g" ]] && continue
    case "$g" in
        .NETStandard1.*) fail "nuspec has a $g dependency group" ;;
        .NETFramework*) fail "nuspec has a $g dependency group" ;;
    esac
done <<<"$groups"

# Every lib/<tfm>/ folder must not be netstandard1.x.
if grep -q '^lib/netstandard1\.' <<<"$files"; then
    fail "package ships a lib/netstandard1.x assembly"
fi
grep -q '^lib/netstandard2\.0/Resultful\.dll$' <<<"$files" || fail "missing lib/netstandard2.0/Resultful.dll"

# OneOf floor, in every group.
oneof_versions="$(grep -o 'id="OneOf" version="[^"]*"' <<<"$nuspec" | sed 's/.*version="\(.*\)"/\1/' | tr -d '[]()' || true)"
[[ -n "$oneof_versions" ]] || fail "nuspec has no OneOf dependency"
while IFS= read -r v; do
    [[ -z "$v" ]] && continue
    lowest="$(printf '%s\n%s\n' "$v" "$min_oneof" | sort -V | head -1)"
    [[ "$lowest" == "$min_oneof" ]] || fail "OneOf floor $v is below $min_oneof"
done <<<"$oneof_versions"

# Metadata.
grep -q '<license type="expression">MIT</license>' <<<"$nuspec" || fail "missing MIT license expression"
grep -q '<readme>README.md</readme>' <<<"$nuspec" || fail "missing readme metadata"
grep -q '^README.md$' <<<"$files" || fail "README.md not packed"
grep -q '<repository type="git" url="https://github.com/Resultful/Resultful.git"' <<<"$nuspec" || fail "missing repository url"
grep -q '<repository [^>]*commit="[0-9a-f]\{40\}"' <<<"$nuspec" || fail "repository metadata has no commit (SourceLink)"

# Symbols package next to the nupkg.
snupkg="${nupkg%.nupkg}.snupkg"
[[ -f "$snupkg" ]] || fail "missing symbols package $(basename "$snupkg")"

if ((${#failures[@]})); then
    echo "Package check FAILED for $(basename "$nupkg"):" >&2
    printf '  - %s\n' "${failures[@]}" >&2
    exit 1
fi

echo "Package check passed for $(basename "$nupkg")"
echo "  dependency groups: $(tr '\n' ' ' <<<"$groups")"
echo "  OneOf: $(tr '\n' ' ' <<<"$oneof_versions")"
