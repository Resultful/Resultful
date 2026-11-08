#!/usr/bin/env sh
# The specifications' own examples, at pinned commits (decision Q51), fetched rather than committed. OfficialExamplesTests
# measures Matchmark against whatever is in the directory given (default: this one) when MATCHMARK_EXAMPLES names it:
#   sh samples/examples/fetch.sh && MATCHMARK_EXAMPLES=$PWD/samples/examples dotnet test Matchmark.Generator.Tests --filter OfficialExamplesTests
# Moving a pin is a change of its own, with the measurement before and after.
set -eu
dir="${1:-$(dirname "$0")}"
fetch() { # repository, commit, folder
  target="$dir/$(echo "$1" | tr '/' '_')"
  rm -rf "$target" && git init -q "$target"
  git -C "$target" fetch -q --depth 1 "https://github.com/$1" "$2"
  git -C "$target" checkout -q FETCH_HEAD -- "$3"
  rm -rf "$target/.git"
}
fetch OAI/learn.openapis.org 82b82f372aa185a07ea11bc158619895ab21f741 examples
fetch asyncapi/spec 41662743fe64189858f4076f27d2b1d9da6b3093 examples
fetch OAI/Arazzo-Specification e9ee1c49414780441fef0c5196c52f8222357a0a examples
fetch OAI/Overlay-Specification be39b56020da12b8db56080da90fecb470701831 compliant-sets
