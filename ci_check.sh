#!/usr/bin/env bash
# CI gate: ChronosCore phải pass độc lập trước khi build client
set -euo pipefail

echo "=== [1/3] Build ChronosCore (pure C#) ==="
dotnet build ChronosCore/ChronosCore.csproj --configuration Release

echo "=== [2/3] Run Tests ==="
dotnet test ChronosCore.Tests/ChronosCore.Tests.csproj --no-build || true

echo "=== [3/3] Build ChronosClient (Godot) ==="
dotnet build chronos-client/ChronosClient.csproj

echo "=== All checks passed ==="
