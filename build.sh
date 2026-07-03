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
  dotnet test tests/SalmonEgg.Domain.Tests --configuration Release --no-build || return 1
  dotnet test tests/SalmonEgg.Application.Tests --configuration Release --no-build || return 1
  dotnet test tests/SalmonEgg.Infrastructure.Tests --configuration Release --no-build || return 1
  # Presentation tests exercise global UI/testhost state; keep the release gate deterministic.
  dotnet test tests/SalmonEgg.Presentation.Core.Tests --configuration Release --no-build -- RunConfiguration.DisableParallelization=true || return 1
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
