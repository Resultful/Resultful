#!/usr/bin/env bash
dotnet tool restore
dotnet fake build -t Publish
