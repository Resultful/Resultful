#!/usr/bin/env bash
dotnet restore
mono packages/build-deps/FAKE/tools/FAKE.exe build.fsx Publish
