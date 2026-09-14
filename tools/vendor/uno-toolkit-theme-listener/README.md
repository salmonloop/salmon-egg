# Uno Toolkit ThemeListener compatibility packages

Toolkit 7.1.206's `ThemeListener` dereferences `Window.Current.CoreWindow`, which is absent
on Skia. Rendering an ordinary Markdown response can therefore raise a native dispatcher
exception. [Upstream PR 242](https://github.com/unoplatform/Uno.WindowsCommunityToolkit/pull/242)
uses the native `Window.Activated` event instead.

These three packages are unchanged outputs from the upstream public
[build 233351](https://dev.azure.com/uno-platform/1dd81cbd-cb35-41de-a570-b0df3571a196/_build/results?buildId=233351),
commit `15b1194ecbf27fa8279647cdc7b8dad8bc14eb62`, artifact `WCT-Packages`.
They were downloaded again from that build and compared with the recorded SHA-256 hashes.
`LICENSE.md` is the upstream MIT license from the same commit. Package archives also include
their own license and third-party notices.

PR 242 is still open; these are development CI artifacts, not a published stable release.

The dependency override is limited to non-Windows application targets and these exact three
package IDs. All other packages use nuget.org. The existing Markdown package remains at 7.1.206;
the direct UI reference supplies its fixed transitive implementation and matching dependencies.

Delete this directory, its NuGet source mapping and the direct UI version pin once a published
Toolkit version includes PR 242. Upgrade the Markdown dependency to that release, then verify
ordinary Markdown rendering in Desktop and BrowserWasm with no `ThemeListener` exception.

| Package | SHA-256 |
| --- | --- |
| Uno.CommunityToolkit.Common.7.1.207-dev.4.g15b1194ecb.nupkg | 88e90eff30c53940e94d6d2daaccdd2a44ec52ac226dfa8b3508af1cf1743720 |
| Uno.CommunityToolkit.WinUI.7.1.207-dev.4.g15b1194ecb.nupkg | 918efc2ce167b28c7af2db0da67fa7bc4aa7a442e162e08f54fe027817dff01a |
| Uno.CommunityToolkit.WinUI.UI.7.1.207-dev.4.g15b1194ecb.nupkg | 76211e5cc4838e7232dc915d4b4b79c0c1de4d4b2d14c4b9ae37b35deb64091c |
