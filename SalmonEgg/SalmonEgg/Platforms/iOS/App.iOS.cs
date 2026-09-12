using Foundation;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Platforms.iOS;
using UIKit;

namespace SalmonEgg;

public partial class App
{
    public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
    {
        // UIKit constructs the delegate directly; its host factory is only used for the App type.
        // Install the UIKit resource boundary before Uno's launch callback constructs the shell.
        var navigationResources = new NavigationResources();
        Resources.MergedDictionaries.Add(navigationResources);
        Resources[typeof(NavigationViewItem)] = navigationResources["IosNavigationViewItemStyle"];
        return base.FinishedLaunching(application, launchOptions);
    }
}
