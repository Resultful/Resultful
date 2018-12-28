#!/usr/bin/env bash
dotnet tool install fake-cli --version 5.10.1 -g
export PATH="$HOME/.dotnet/tools:$PATH"
fake -v build  -t Publish
