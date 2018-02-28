#!/usr/bin/env bash
export FrameworkPathOverride=/usr/lib/mono/4.5/
dotnet restore
mono packages/build-deps/FAKE/tools/FAKE.exe build.fsx Publish
