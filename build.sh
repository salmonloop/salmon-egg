#!/bin/bash

echo "========================================"
echo "SalmonEgg Build Script"
echo "========================================"
echo

echo "[1/4] Restoring dependencies..."
dotnet restore SalmonEgg.sln || exit 1

echo
echo "[2/4] Building project..."
dotnet build SalmonEgg.sln --configuration Release --no-restore || exit 1

echo
echo "[3/4] Running tests..."
dotnet test SalmonEgg.sln --configuration Release --no-build || exit 1

echo
echo "[4/4] Publishing application..."
dotnet publish SalmonEgg/SalmonEgg/SalmonEgg.csproj \
  --configuration Release \
  --framework net10.0-desktop \
  --output publish/windows-desktop \
  --no-build

echo
echo "========================================"
echo "Build completed successfully!"
echo "Output: publish/windows-desktop/"
echo "========================================"
