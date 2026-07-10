#!/bin/bash

desktop_publish_dir() {
  case "$(uname -s)" in
    Linux )
      echo "publish/linux-desktop"
      ;;
    Darwin )
      echo "publish/macos-desktop"
      ;;
    * )
      echo "publish/desktop"
      ;;
  esac
}

run_test_gates() {
  dotnet test --project tests/SalmonEgg.Domain.Tests/SalmonEgg.Domain.Tests.csproj --configuration Release --no-build --timeout 3m || return 1
  dotnet test --project tests/SalmonEgg.Application.Tests/SalmonEgg.Application.Tests.csproj --configuration Release --no-build --timeout 3m || return 1
  dotnet test --project tests/SalmonEgg.Infrastructure.Tests/SalmonEgg.Infrastructure.Tests.csproj --configuration Release --no-build --timeout 5m || return 1
  # Presentation.Core parallelization is controlled by testconfig.json for MTP.
  dotnet test --project tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --configuration Release --no-build --timeout 5m || return 1
}

case "$1" in
  ""|desktop )
    output_dir="$(desktop_publish_dir)"

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
    run_test_gates || exit 1

    echo
    echo "[4/4] Publishing application..."
    dotnet publish SalmonEgg/SalmonEgg/SalmonEgg.csproj \
      --configuration Release \
      --framework net10.0-desktop \
      --output "${output_dir}" \
      --no-build

    echo
    echo "========================================"
    echo "Build completed successfully!"
    echo "Output: ${output_dir}/"
    echo "========================================"
    ;;
  msix )
    echo "MSIX packaging is only supported on Windows. Use build.bat msix."
    exit 1
    ;;
  wasm )
    echo "========================================"
    echo "SalmonEgg WebAssembly Build"
    echo "========================================"
    echo

    echo "[1/3] Restoring dependencies..."
    dotnet restore SalmonEgg.sln || exit 1

    echo
    echo "[2/3] Publishing WebAssembly..."
    dotnet publish SalmonEgg/SalmonEgg/SalmonEgg.csproj \
      --configuration Release \
      --framework net10.0-browserwasm \
      --output publish/wasm \
      -p:PublishTrimmed=true \
      -p:TrimMode=link || exit 1

    echo
    echo "========================================"
    echo "WebAssembly build completed!"
    echo "Output: publish/wasm/wwwroot/"
    echo "========================================"
    ;;
  -h|--help )
    echo "Usage:"
    echo "  ./build.sh           (default: desktop release build)"
    echo "  ./build.sh desktop   (desktop release build)"
    echo "  ./build.sh msix      (Windows only)"
    echo "  ./build.sh wasm     (build WebAssembly)"
    ;;
  * )
    echo "Unknown option: $1"
    echo "Use ./build.sh --help"
    exit 1
    ;;
esac
