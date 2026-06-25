#!/bin/bash
files=(
"SalmonEgg/SalmonEgg/Presentation/Converters/MessageDirectionToCornerRadiusConverter.cs"
"SalmonEgg/SalmonEgg/Presentation/Converters/MessageDirectionToBorderThicknessConverter.cs"
"SalmonEgg/SalmonEgg/Presentation/Converters/MessageDirectionToBackgroundConverter.cs"
"SalmonEgg/SalmonEgg/Presentation/Converters/MessageDirectionToAlignmentConverter.cs"
"SalmonEgg/SalmonEgg/Presentation/Converters/InverseBooleanConverter.cs"
"SalmonEgg/SalmonEgg/Presentation/Converters/InverseBoolConverter.cs"
"SalmonEgg/SalmonEgg/Presentation/Converters/ConnectionStatusToColorConverter.cs"
"SalmonEgg/SalmonEgg/Presentation/Converters/BoolToVisibilityConverter.cs"
"SalmonEgg/SalmonEgg/Controls/ChatSkeleton.xaml.cs"
"SalmonEgg/SalmonEgg/Presentation/Views/GeneralSettingsPage.xaml.cs"
)

for file in "${files[@]}"; do
    sed -i 's/^namespace \(.*\)/namespace \1;/' "$file"
    sed -i '0,/^{/s/^{//' "$file"
    sed -i '$ d' "$file"
done
