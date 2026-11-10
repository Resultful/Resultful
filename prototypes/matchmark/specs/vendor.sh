#!/usr/bin/env sh
# The specifications Matchmark reads, vendored at pinned commits (decision Q56): each one's text, its official JSON
# Schemas, and its own examples and test fixtures, which the reader projects' regression suites read. Run from this
# folder to refresh it: sh vendor.sh. Moving a pin is a change of its own, with the regression suites run before and after.
set -eu
here="$(cd "$(dirname "$0")" && pwd)"
work="$(mktemp -d)"
manifest="$here/SOURCES.md"
printf '# Sources\n\nEvery file under specs/ comes from one of these repositories, at the commit given, under its licence (each\nfolder holds a copy). Written by vendor.sh; do not edit by hand.\n\n| Folder | Repository | Commit | Paths |\n|:--|:--|:--|:--|\n' > "$manifest"

fetch() { # repository commit -> checkout dir
  dir="$work/$(echo "$1-$2" | tr '/' '_')"
  if [ ! -d "$dir" ]; then
    git init -q "$dir"
    git -C "$dir" fetch -q --depth 1 "https://github.com/$1" "$2"
    git -C "$dir" checkout -q FETCH_HEAD
  fi
  echo "$dir"
}

take() { # repository commit source target
  dir="$(fetch "$1" "$2")"
  mkdir -p "$(dirname "$here/$4")"
  rm -rf "$here/$4"
  cp -r "$dir/$3" "$here/$4"
  printf '| `%s` | [%s](https://github.com/%s) | `%s` | `%s` |\n' "$4" "$1" "$1" "$2" "$3" >> "$manifest"
}

licence() { # repository commit folder
  dir="$(fetch "$1" "$2")"
  mkdir -p "$here/$3"
  cp "$dir/LICENSE" "$here/$3/LICENSE"
  [ -f "$dir/NOTICE" ] && cp "$dir/NOTICE" "$here/$3/NOTICE" || true
}

# AsyncAPI: the text of each version from its release tag, the bundled JSON Schemas, the real-world examples, and the
# schemas' per-object examples.
licence asyncapi/spec 41662743fe64189858f4076f27d2b1d9da6b3093 asyncapi
take asyncapi/spec 1824379ba6252bfb52550337a86ca9b10a33b3aa spec/asyncapi.md asyncapi/text/2.6.0.md
take asyncapi/spec d78dcea70c9b094a3df72f9a4e811828cec778cc spec/asyncapi.md asyncapi/text/3.0.0.md
take asyncapi/spec b3fac5bb522771428ea57b16129b273cd3ea0180 spec/asyncapi.md asyncapi/text/3.1.0.md
take asyncapi/spec 41662743fe64189858f4076f27d2b1d9da6b3093 examples asyncapi/examples
for v in 2.6.0 3.0.0 3.1.0; do
  take asyncapi/spec-json-schemas eee8d2217d23b22c475090c0e12f20801f531f11 "schemas/$v-without-\$id.json" "asyncapi/schemas/$v.json"
  take asyncapi/spec-json-schemas eee8d2217d23b22c475090c0e12f20801f531f11 "examples/$v" "asyncapi/object-examples/$v"
done

# Arazzo: the texts and examples from main, the 1.0 schema from its archive, and the 1.1 schema and its pass and fail
# fixtures from the 1.1 development branch, where they live until 1.1 is published.
licence OAI/Arazzo-Specification 12f619ecd85e79fb67ce0610b85926ca3cfd9c79 arazzo
take OAI/Arazzo-Specification 12f619ecd85e79fb67ce0610b85926ca3cfd9c79 versions arazzo/text
take OAI/Arazzo-Specification 12f619ecd85e79fb67ce0610b85926ca3cfd9c79 examples arazzo/examples
take OAI/Arazzo-Specification 12f619ecd85e79fb67ce0610b85926ca3cfd9c79 _archive_/schemas/v1.0/schema.json arazzo/schemas/1.0.json
take OAI/Arazzo-Specification aece737bc149c4564c6138962ecc9e026b3752f9 src/schemas/validation/schema.yaml arazzo/schemas/1.1.yaml
take OAI/Arazzo-Specification aece737bc149c4564c6138962ecc9e026b3752f9 tests/schema arazzo/tests/1.1

# Overlay: the texts, the schemas, the compliant sets (input, overlay and the output it must give), and the schemas'
# pass and fail fixtures for each version.
licence OAI/Overlay-Specification be39b56020da12b8db56080da90fecb470701831 overlay
take OAI/Overlay-Specification be39b56020da12b8db56080da90fecb470701831 versions overlay/text
take OAI/Overlay-Specification be39b56020da12b8db56080da90fecb470701831 schemas overlay/schemas
take OAI/Overlay-Specification be39b56020da12b8db56080da90fecb470701831 compliant-sets overlay/compliant-sets
take OAI/Overlay-Specification be39b56020da12b8db56080da90fecb470701831 tests overlay/tests
rm -f "$here/overlay/tests/schema.test.mjs"

# OpenAPI: read by Microsoft.OpenApi, not a reader of Matchmark's own, but its examples are the official-examples
# measurement's (Q51), so they're kept here with the rest rather than fetched.
licence OAI/learn.openapis.org 82b82f372aa185a07ea11bc158619895ab21f741 openapi
take OAI/learn.openapis.org 82b82f372aa185a07ea11bc158619895ab21f741 examples openapi/examples

rm -rf "$work"
echo "vendored into $here"
