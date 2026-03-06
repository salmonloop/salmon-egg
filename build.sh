#!/bin/bash

echo "========================================"
echo "Uno ACP Client Build Script"
echo "========================================"
echo

echo "[1/4] Restoring dependencies..."
dotnet restore UnoAcpClient.sln || exit 1

echo
echo "[2/4] Building project..."
dotnet build UnoAcpClient.sln --configuration Release --no-restore || exit 1

echo
echo "[3/4] Running tests..."
dotnet test UnoAcpClient.sln --configuration Release --no-build || exit 1

echo
echo "[4/4] Publishing application..."
dotnet publish UnoAcpClient/UnoAcpClient/UnoAcpClient.csproj \
  --configuration Release \
  --framework net9.0-desktop \
  --output publish/windows-desktop \
  --no-build

echo
echo "========================================"
echo "Build completed successfully!"
echo "Output: publish/windows-desktop/"
echo "========================================"
