#!/bin/bash
echo "Starting SalmonEgg..."

case "$1" in
  "" )
    dotnet run --project SalmonEgg/SalmonEgg/SalmonEgg.csproj --framework net10.0-desktop
    ;;
  desktop )
    dotnet run --project SalmonEgg/SalmonEgg/SalmonEgg.csproj --framework net10.0-desktop
    ;;
  msix )
    echo "MSIX packaging is only supported on Windows. Use run.bat msix."
    exit 1
    ;;
  -h|--help )
    echo "Usage:"
    echo "  ./run.sh           (default: desktop)"
    echo "  ./run.sh desktop   (dotnet run net10.0-desktop)"
    echo "  ./run.sh msix      (Windows only)"
    ;;
  * )
    echo "Unknown option: $1"
    echo "Use ./run.sh --help"
    exit 1
    ;;
esac
