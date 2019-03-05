#!/usr/bin/env bash
dotnet tool install fake-cli --version 5.11.1 -g
dotnet tool install paket -g --version=5.198.0
export PATH="$HOME/.dotnet/tools:$PATH"
fake build  -t Publish
