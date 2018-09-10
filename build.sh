#!/usr/bin/env bash
dotnet tool install fake-cli --version 5.6.1 --tool-path .fake/tool-path
.fake/tool-path/fake build -v -t Publish
