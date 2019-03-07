#!/usr/bin/env bash
dotnet tool install fake-cli -g --version=5.12.4
dotnet tool install paket -g --version=5.198.0
export PATH="$HOME/.dotnet/tools:$PATH"
fake build  -t Publish
