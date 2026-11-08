#!/usr/bin/env sh
# Large public specs, fetched rather than committed (their size, and their licences). LargeSpecTests runs over whatever
# is in the directory given (default: this one) when MATCHMARK_LARGE_SPECS names it:
#   sh samples/large/fetch.sh && MATCHMARK_LARGE_SPECS=$PWD/samples/large dotnet test Matchmark.Generator.Tests --filter LargeSpecTests
set -eu
dir="${1:-$(dirname "$0")}"
curl -sSL -o "$dir/github.json" https://raw.githubusercontent.com/github/rest-api-description/main/descriptions/api.github.com/api.github.com.json
curl -sSL -o "$dir/stripe.json" https://raw.githubusercontent.com/stripe/openapi/master/openapi/spec3.json
