#!/usr/bin/env bash
dotnet tool install fake-cli -g --version 5.6.1
fake build -t Publish
