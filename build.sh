#!/usr/bin/env bash
export FrameworkPathOverride=/usr/lib/mono/4.7-api/
dotnet tool install fake-cli -g --version 5.6.1S
fake build -t Publish
