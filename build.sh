#!/usr/bin/env bash
dotnet tool install fake-cli --version 5.11.1 -g
export PATH="$HOME/.dotnet/tools:$PATH"
fake build  -t Publish
