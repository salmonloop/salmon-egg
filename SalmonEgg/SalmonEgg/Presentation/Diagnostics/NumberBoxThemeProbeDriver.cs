using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SalmonEgg.Presentation.Models.Settings;
using SalmonEgg.Presentation.ViewModels.Navigation;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace SalmonEgg.Presentation.Diagnostics;

/// <summary>
/// Diagnostics-only runtime probe for the Skia NumberBox focused-text contrast regression.
/// </summary>
/// <remarks>
/// The driver enters Data &amp; Storage through the authoritative navigation ViewModel, focuses the
/// real NumberBox input, and samples only realized template facts. It is compiled out of Release
/// behavior and inert unless <c>SALMONEGG_NUMBERBOX_THEME_PROBE=1</c>.
/// </remarks>
internal static class NumberBoxThemeProbeDriver
{
    private const string EnableVariable = "SALMONEGG_NUMBERBOX_THEME_PROBE";
    private const string NumberBoxAutomationId = "DataStorage.CacheRetention";
    private const string FocusSinkAutomationId = "DataStorage.SaveLocalHistory";
    private const string InputBoxName = "InputBox";
    private const string ContentElementName = "ContentElement";
    private const string BorderElementName = "BorderElement";
    private const int SampleCount = 3;
    private const int SampleAttemptCount = 9;
    private const int FocusAttemptCount = 10;
    private const int FocusSettleDelayMilliseconds = 100;
    private const int SampleSettleDelayMilliseconds = 300;
    private const int RenderCaptureAttemptCount = 5;
    private const int RenderCaptureRetryDelayMilliseconds = 100;
    private const int ViewportSettleAttemptCount = 20;
    private const int ViewportSettleDelayMilliseconds = 50;
    private const double MinimumBackgroundSampleInset = 6;
    private const int BackgroundClusterTolerance = 6;
    private const double MinimumContrastRatio = 4.5;
#if DEBUG && __UNO_SKIA__
    private static int _started;
#endif

    public static void TryStart(IServiceProvider services, DependencyObject shellRoot)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(shellRoot);

#if DEBUG && __UNO_SKIA__
        if (!string.Equals(Environment.GetEnvironmentVariable(EnableVariable), "1", StringComparison.Ordinal)
            || Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        var navigation = services.GetRequiredService<MainNavigationViewModel>();
        _ = RunAsync(navigation, shellRoot);
#endif
    }

#if DEBUG && __UNO_SKIA__
    private static async Task RunAsync(MainNavigationViewModel navigation, DependencyObject shellRoot)
    {
        var result = NumberBoxThemeProbeRunResult.NotStarted;

        try
        {
            App.BootLog("NumberBoxThemeProbe: started");
            var targets = await ResolveProbeTargetsAsync(navigation, shellRoot).ConfigureAwait(true);
            result = targets is null
                ? NumberBoxThemeProbeRunResult.TargetUnavailable
                : await CollectSamplesAsync(targets).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            result = NumberBoxThemeProbeRunResult.Faulted;
            App.BootLog($"NumberBoxThemeProbe: faulted {ex}");
        }
        finally
        {
            App.BootLog(
                $"NumberBoxThemeProbe: complete samples={result.CompletedSamples}"
                + $" valueUnchanged={result.ValueUnchanged} passed={result.Passed}"
                + $" reason={result.FailureReason}");
        }
    }

    private static async Task<NumberBoxThemeProbeTargets?> ResolveProbeTargetsAsync(
        MainNavigationViewModel navigation,
        DependencyObject shellRoot)
    {
        if (!await navigation.ActivateSettingsAsync(SettingsSectionCatalog.DataStorageKey).ConfigureAwait(true))
        {
            return null;
        }

        var numberBox = await WaitForDescendantAsync<NumberBox>(
                shellRoot,
                static candidate => string.Equals(
                    AutomationProperties.GetAutomationId(candidate),
                    NumberBoxAutomationId,
                    StringComparison.Ordinal),
                TimeSpan.FromSeconds(8))
            .ConfigureAwait(true);
        var focusSink = await WaitForDescendantAsync<ToggleSwitch>(
                shellRoot,
                static candidate => string.Equals(
                    AutomationProperties.GetAutomationId(candidate),
                    FocusSinkAutomationId,
                    StringComparison.Ordinal),
                TimeSpan.FromSeconds(8))
            .ConfigureAwait(true);
        if (numberBox is null || focusSink is null)
        {
            return null;
        }

        numberBox.StartBringIntoView();
        numberBox.ApplyTemplate();
        numberBox.UpdateLayout();
        var inputBox = await WaitForDescendantAsync<TextBox>(
                numberBox,
                static candidate => string.Equals(candidate.Name, InputBoxName, StringComparison.Ordinal),
                TimeSpan.FromSeconds(5))
            .ConfigureAwait(true);

        return inputBox is null
            ? null
            : new NumberBoxThemeProbeTargets(numberBox, inputBox, focusSink);
    }

    private static async Task<NumberBoxThemeProbeRunResult> CollectSamplesAsync(
        NumberBoxThemeProbeTargets targets)
    {
        var originalValue = targets.NumberBox.Value;
        var allSamplesPassed = await TryMoveKeyboardFocusAsync(
                targets.InputBox,
                () => targets.InputBox.FocusState != FocusState.Unfocused)
            .ConfigureAwait(true);

        // The first Skia render after Settings navigation can expose the realized template before
        // its backdrop has a complete frame. Warm the same native focus path once before counting
        // samples so the three observations measure the settled control, not first-frame timing.
        _ = await TryMoveKeyboardFocusAsync(
                targets.FocusSink,
                () => targets.FocusSink.FocusState != FocusState.Unfocused
                    && targets.InputBox.FocusState == FocusState.Unfocused)
            .ConfigureAwait(true);
        _ = await TryMoveKeyboardFocusAsync(
                targets.InputBox,
                () => targets.InputBox.FocusState != FocusState.Unfocused)
            .ConfigureAwait(true);
        await Task.Delay(SampleSettleDelayMilliseconds).ConfigureAwait(true);

        var completedSamples = 0;
        for (var sampleAttempt = 1;
            sampleAttempt <= SampleAttemptCount && completedSamples < SampleCount;
            sampleAttempt++)
        {
            var unfocusedBeforeSample = await TryMoveKeyboardFocusAsync(
                    targets.FocusSink,
                    () => targets.FocusSink.FocusState != FocusState.Unfocused
                        && targets.InputBox.FocusState == FocusState.Unfocused)
                .ConfigureAwait(true);

            var inputFocused = await TryMoveKeyboardFocusAsync(
                    targets.InputBox,
                    () => targets.InputBox.FocusState != FocusState.Unfocused)
                .ConfigureAwait(true);
            await Task.Delay(SampleSettleDelayMilliseconds).ConfigureAwait(true);

            targets.NumberBox.UpdateLayout();
            targets.InputBox.UpdateLayout();

            // Focusing the sink scrolls the page away from the NumberBox, and BringIntoView only
            // requests a scroll: it does not complete before the next layout pass. Capturing here
            // without waiting samples a rect that still lies outside the render root, which yields
            // an empty region indistinguishable from an unreadable frame. Wait for the realized
            // geometry to land inside the render root before sampling pixels.
            var boundsSettled = await WaitForSampleBoundsInsideRenderRootAsync(
                    targets.NumberBox,
                    targets.InputBox)
                .ConfigureAwait(true);
            var snapshot = await CaptureSnapshotAsync(
                    targets.NumberBox,
                    targets.InputBox,
                    unfocusedBeforeSample && inputFocused,
                    boundsSettled)
                .ConfigureAwait(true);
            if (snapshot.Foreground is null || snapshot.Background is null)
            {
                App.BootLog(snapshot.FormatUnavailable(sampleAttempt));
                continue;
            }

            completedSamples++;
            allSamplesPassed &= snapshot.Passed;
            App.BootLog(snapshot.Format(completedSamples));
        }

        var valueUnchanged = targets.NumberBox.Value.Equals(originalValue);
        var collectedRequiredSamples = completedSamples == SampleCount;
        var passed = collectedRequiredSamples && allSamplesPassed && valueUnchanged;
        var failureReason = !collectedRequiredSamples
            ? "capture-unavailable"
            : !allSamplesPassed
            ? "sample-invariant"
            : valueUnchanged
                ? "none"
                : "value-changed";
        return new NumberBoxThemeProbeRunResult(completedSamples, valueUnchanged, passed, failureReason);
    }

    private static async Task<bool> TryMoveKeyboardFocusAsync(Control target, Func<bool> reachedExpectedState)
    {
        for (var attempt = 0; attempt < FocusAttemptCount; attempt++)
        {
            target.StartBringIntoView();
            target.ApplyTemplate();
            target.UpdateLayout();
            _ = target.Focus(FocusState.Keyboard);
            await Task.Delay(FocusSettleDelayMilliseconds).ConfigureAwait(true);
            if (reachedExpectedState())
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<NumberBoxThemeProbeSnapshot> CaptureSnapshotAsync(
        NumberBox numberBox,
        TextBox inputBox,
        bool focusTransitionSucceeded,
        bool boundsSettled)
    {
        var contentElement = FindDescendant<ScrollViewer>(
            inputBox,
            static candidate => string.Equals(candidate.Name, ContentElementName, StringComparison.Ordinal));
        var borderElement = FindDescendant<Border>(
            inputBox,
            static candidate => string.Equals(candidate.Name, BorderElementName, StringComparison.Ordinal));

        var renderedColors = await TryCaptureRenderedColorsAsync(contentElement, borderElement)
            .ConfigureAwait(true);
        var background = renderedColors.Background;
        var foreground = renderedColors.Foreground;
        var contrast = foreground is not null && background is not null
            ? CalculateContrastRatio(foreground.Value, background.Value)
            : 0;
        var focused = inputBox.FocusState != FocusState.Unfocused;
        var visible = IsEffectivelyVisible(numberBox) && IsEffectivelyVisible(inputBox);
        var numberBoxTheme = numberBox.ActualTheme;
        var inputTheme = inputBox.ActualTheme;
        var contentTheme = contentElement?.ActualTheme ?? ElementTheme.Default;
        var borderTheme = borderElement?.ActualTheme ?? ElementTheme.Default;
        var passed = visible
            && focusTransitionSucceeded
            && focused
            && numberBoxTheme == ElementTheme.Dark
            && inputTheme == ElementTheme.Dark
            && contentTheme == ElementTheme.Dark
            && borderTheme == ElementTheme.Dark
            && contrast >= MinimumContrastRatio;

        return new NumberBoxThemeProbeSnapshot(
            visible,
            focusTransitionSucceeded,
            focused,
            inputBox.FocusState,
            numberBoxTheme,
            inputTheme,
            contentTheme,
            borderTheme,
            foreground,
            background,
            contrast,
            renderedColors.Attempts,
            boundsSettled,
            renderedColors.Diagnostic,
            passed);
    }

    private static async Task<RenderedColors> TryCaptureRenderedColorsAsync(
        ScrollViewer? contentElement,
        Border? borderElement)
    {
        if (contentElement is null || borderElement is null)
        {
            return RenderedColors.Unavailable;
        }

        Windows.UI.Color? background = null;
        Windows.UI.Color? foreground = null;
        var diagnostic = "not-attempted";
        string? firstFailure = null;
        for (var attempt = 1; attempt <= RenderCaptureAttemptCount; attempt++)
        {
            borderElement.UpdateLayout();
            var capture = await TryCaptureRenderedBackgroundAsync(borderElement).ConfigureAwait(true);
            background = capture.Color;
            diagnostic = capture.Diagnostic;
            foreground = background is null
                ? null
                : TryResolveEffectiveForeground(contentElement.Foreground, background.Value);
            if (background is not null && foreground is not null)
            {
                return new RenderedColors(
                    foreground,
                    background,
                    attempt,
                    firstFailure is null ? diagnostic : $"{diagnostic} recoveredFrom=[{firstFailure}]");
            }

            if (background is not null)
            {
                diagnostic = "foreground-brush-unreadable";
            }

            firstFailure ??= diagnostic;

            if (attempt < RenderCaptureAttemptCount)
            {
                // RenderTargetBitmap can transiently return an incomplete Skia frame immediately
                // after a native focus-state transition. Retry the same realized control instead
                // of treating an unavailable frame as evidence of unreadable colors. Re-request the
                // scroll as well: a delay alone cannot move a control that is still out of view.
                borderElement.StartBringIntoView();
                await Task.Delay(RenderCaptureRetryDelayMilliseconds).ConfigureAwait(true);
            }
        }

        return new RenderedColors(foreground, background, RenderCaptureAttemptCount, diagnostic);
    }

    /// <summary>
    /// Captures the rendered BorderElement background, or an unavailable sample that names the
    /// reason. A bare <c>null</c> cannot distinguish an empty Skia frame from an off-viewport rect,
    /// a translucent offscreen composition or an ambiguous cluster, so every exit reports its cause.
    /// </summary>
    private static async Task<BackgroundCapture> TryCaptureRenderedBackgroundAsync(
        Border borderElement)
    {
        if (borderElement.DispatcherQueue is null || !borderElement.DispatcherQueue.HasThreadAccess)
        {
            return BackgroundCapture.Unavailable("no-dispatcher-access");
        }

        var renderRoot = FindVisualRoot(borderElement);
        if (renderRoot is null || renderRoot.RenderSize.Width <= 0 || renderRoot.RenderSize.Height <= 0)
        {
            return BackgroundCapture.Unavailable(
                $"render-root-unmeasured root={FormatSize(renderRoot?.RenderSize)}");
        }

        // Skia Acrylic samples its ancestor backdrop. Rendering only BorderElement, or rebuilding
        // brush layers from properties, would omit that composition and can produce a false contrast.
        var sampleBounds = TryResolveSampleBounds(borderElement, renderRoot);
        if (sampleBounds is null)
        {
            return BackgroundCapture.Unavailable(
                $"sample-bounds-unresolved border={FormatSize(new Size(borderElement.ActualWidth, borderElement.ActualHeight))}");
        }

        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(renderRoot);
        if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
        {
            return BackgroundCapture.Unavailable(
                $"render-empty bitmap={bitmap.PixelWidth}x{bitmap.PixelHeight}"
                + $" root={FormatSize(renderRoot.RenderSize)}");
        }

        var pixelBuffer = await bitmap.GetPixelsAsync();
        var expectedLength = checked(bitmap.PixelWidth * bitmap.PixelHeight * 4);
        if (pixelBuffer.Length != (uint)expectedLength)
        {
            return BackgroundCapture.Unavailable(
                $"pixel-buffer-mismatch length={pixelBuffer.Length} expected={expectedLength}"
                + $" bitmap={bitmap.PixelWidth}x{bitmap.PixelHeight}");
        }

        var pixels = new byte[expectedLength];
        using (var reader = DataReader.FromBuffer(pixelBuffer))
        {
            reader.ReadBytes(pixels);
        }

        var scaleX = bitmap.PixelWidth / renderRoot.RenderSize.Width;
        var scaleY = bitmap.PixelHeight / renderRoot.RenderSize.Height;
        var bounds = sampleBounds.Value;
        return TrySampleOpaqueBackground(
            pixels,
            bitmap.PixelWidth,
            bitmap.PixelHeight,
            new Rect(
                bounds.X * scaleX,
                bounds.Y * scaleY,
                bounds.Width * scaleX,
                bounds.Height * scaleY));
    }

    /// <summary>
    /// Waits until the inset BorderElement sample region is fully inside the render root, so the
    /// pixel sample measures the realized control instead of an off-viewport empty rect.
    /// </summary>
    private static async Task<bool> WaitForSampleBoundsInsideRenderRootAsync(
        NumberBox numberBox,
        TextBox inputBox)
    {
        for (var attempt = 1; attempt <= ViewportSettleAttemptCount; attempt++)
        {
            numberBox.StartBringIntoView();
            numberBox.UpdateLayout();
            inputBox.UpdateLayout();

            var borderElement = FindDescendant<Border>(
                inputBox,
                static candidate => string.Equals(candidate.Name, BorderElementName, StringComparison.Ordinal));
            var renderRoot = borderElement is null ? null : FindVisualRoot(borderElement);
            if (borderElement is not null
                && renderRoot is not null
                && TryResolveSampleBounds(borderElement, renderRoot) is { } bounds
                && IsInsideRenderRoot(bounds, renderRoot.RenderSize))
            {
                return true;
            }

            await Task.Delay(ViewportSettleDelayMilliseconds).ConfigureAwait(true);
        }

        return false;
    }

    private static bool IsInsideRenderRoot(Rect sampleBounds, Size renderSize)
        => sampleBounds.X >= 0
            && sampleBounds.Y >= 0
            && sampleBounds.X + sampleBounds.Width <= renderSize.Width
            && sampleBounds.Y + sampleBounds.Height <= renderSize.Height;

    /// <summary>
    /// Projects the inset BorderElement region into render-root coordinates, or <c>null</c> when the
    /// realized control is too small to expose an unambiguous background region.
    /// </summary>
    private static Rect? TryResolveSampleBounds(Border borderElement, UIElement renderRoot)
    {
        var horizontalInset = Math.Max(
            MinimumBackgroundSampleInset,
            Math.Max(borderElement.BorderThickness.Left, borderElement.BorderThickness.Right) + 2);
        var verticalInset = Math.Max(
            MinimumBackgroundSampleInset,
            Math.Max(borderElement.BorderThickness.Top, borderElement.BorderThickness.Bottom) + 2);
        if (borderElement.ActualWidth <= horizontalInset * 2
            || borderElement.ActualHeight <= verticalInset * 2)
        {
            return null;
        }

        return borderElement.TransformToVisual(renderRoot).TransformBounds(
            new Rect(
                horizontalInset,
                verticalInset,
                borderElement.ActualWidth - (horizontalInset * 2),
                borderElement.ActualHeight - (verticalInset * 2)));
    }

    private static UIElement? FindVisualRoot(DependencyObject element)
    {
        UIElement? root = element as UIElement;
        DependencyObject? current = element;
        while ((current = VisualTreeHelper.GetParent(current)) is not null)
        {
            if (current is UIElement uiElement)
            {
                root = uiElement;
            }
        }

        return root;
    }

    private static BackgroundCapture TrySampleOpaqueBackground(
        byte[] pixels,
        int pixelWidth,
        int pixelHeight,
        Rect sampleBounds)
    {
        var left = Math.Max(0, (int)Math.Ceiling(sampleBounds.X));
        var top = Math.Max(0, (int)Math.Ceiling(sampleBounds.Y));
        var right = Math.Min(pixelWidth, (int)Math.Floor(sampleBounds.X + sampleBounds.Width));
        var bottom = Math.Min(pixelHeight, (int)Math.Floor(sampleBounds.Y + sampleBounds.Height));
        if (right <= left || bottom <= top)
        {
            return BackgroundCapture.Unavailable(
                $"sample-region-empty rect={FormatRect(sampleBounds)} bitmap={pixelWidth}x{pixelHeight}");
        }

        var regionPixelCount = checked((right - left) * (bottom - top));
        var samples = new List<Windows.UI.Color>(regionPixelCount);
        var translucentPixelCount = 0;
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var offset = ((y * pixelWidth) + x) * 4;
                if (pixels[offset + 3] != byte.MaxValue)
                {
                    translucentPixelCount++;
                    continue;
                }

                samples.Add(Windows.UI.Color.FromArgb(
                    byte.MaxValue,
                    pixels[offset + 2],
                    pixels[offset + 1],
                    pixels[offset]));
            }
        }

        // Preserved semantics: a partially transparent offscreen composition is not evidence of the
        // realized background, so the sample stays unavailable. The counts identify how much of the
        // region failed to composite, which a bare null could not.
        if (translucentPixelCount > 0)
        {
            return BackgroundCapture.Unavailable(
                $"sample-translucent translucent={translucentPixelCount}/{regionPixelCount}"
                + $" rect={FormatRect(sampleBounds)}");
        }

        var reds = new List<byte>(samples.Count);
        var greens = new List<byte>(samples.Count);
        var blues = new List<byte>(samples.Count);
        foreach (var sample in samples)
        {
            reds.Add(sample.R);
            greens.Add(sample.G);
            blues.Add(sample.B);
        }

        reds.Sort();
        greens.Sort();
        blues.Sort();
        var medianIndex = reds.Count / 2;
        var red = reds[medianIndex];
        var green = greens[medianIndex];
        var blue = blues[medianIndex];

        // Text, selection, and spin glyphs are sparse overlays. The rendered background must still
        // own at least two thirds of the inset BorderElement region or the sample is ambiguous.
        var inlierReds = new List<byte>();
        var inlierGreens = new List<byte>();
        var inlierBlues = new List<byte>();
        foreach (var sample in samples)
        {
            if (Math.Abs(sample.R - red) <= BackgroundClusterTolerance
                && Math.Abs(sample.G - green) <= BackgroundClusterTolerance
                && Math.Abs(sample.B - blue) <= BackgroundClusterTolerance)
            {
                inlierReds.Add(sample.R);
                inlierGreens.Add(sample.G);
                inlierBlues.Add(sample.B);
            }
        }

        if (inlierReds.Count * 3 < reds.Count * 2)
        {
            return BackgroundCapture.Unavailable(
                $"sample-cluster-ambiguous inliers={inlierReds.Count}/{reds.Count}"
                + $" median=#{red:X2}{green:X2}{blue:X2}");
        }

        inlierReds.Sort();
        inlierGreens.Sort();
        inlierBlues.Sort();
        var inlierMedianIndex = inlierReds.Count / 2;
        return BackgroundCapture.Available(
            Windows.UI.Color.FromArgb(
                byte.MaxValue,
                inlierReds[inlierMedianIndex],
                inlierGreens[inlierMedianIndex],
                inlierBlues[inlierMedianIndex]),
            $"ok inliers={inlierReds.Count}/{reds.Count} rect={FormatRect(sampleBounds)}");
    }

    private static string FormatRect(Rect rect)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{rect.X:F1},{rect.Y:F1}+{rect.Width:F1}x{rect.Height:F1}");

    private static string FormatSize(Size? size)
        => size is { } value
            ? string.Create(CultureInfo.InvariantCulture, $"{value.Width:F1}x{value.Height:F1}")
            : "<none>";

    private static Windows.UI.Color? TryResolveEffectiveForeground(
        Brush? foreground,
        Windows.UI.Color effectiveBackground)
    {
        var foregroundLayer = TryCreateColorLayer(foreground);
        if (foregroundLayer is null)
        {
            return null;
        }

        var backgroundLayer = new ColorLayer(
            effectiveBackground.R / 255d,
            effectiveBackground.G / 255d,
            effectiveBackground.B / 255d,
            1);
        return ToOpaqueColor(Composite(foregroundLayer.Value, backgroundLayer));
    }

    private static ColorLayer? TryCreateColorLayer(Brush? brush)
    {
        if (brush is not SolidColorBrush solidColorBrush)
        {
            return null;
        }

        var alpha = solidColorBrush.Opacity * (solidColorBrush.Color.A / 255d);
        if (alpha <= 0)
        {
            return null;
        }

        return new ColorLayer(
            solidColorBrush.Color.R / 255d,
            solidColorBrush.Color.G / 255d,
            solidColorBrush.Color.B / 255d,
            alpha);
    }

    private static ColorLayer Composite(ColorLayer foreground, ColorLayer background)
    {
        var alpha = foreground.Alpha + (background.Alpha * (1 - foreground.Alpha));
        if (alpha <= 0)
        {
            return default;
        }

        return new ColorLayer(
            ((foreground.Red * foreground.Alpha)
                + (background.Red * background.Alpha * (1 - foreground.Alpha))) / alpha,
            ((foreground.Green * foreground.Alpha)
                + (background.Green * background.Alpha * (1 - foreground.Alpha))) / alpha,
            ((foreground.Blue * foreground.Alpha)
                + (background.Blue * background.Alpha * (1 - foreground.Alpha))) / alpha,
            alpha);
    }

    private static Windows.UI.Color ToOpaqueColor(ColorLayer layer)
        => Windows.UI.Color.FromArgb(
            byte.MaxValue,
            ToByte(layer.Red),
            ToByte(layer.Green),
            ToByte(layer.Blue));

    private static byte ToByte(double value)
        => (byte)Math.Clamp(Math.Round(value * 255), byte.MinValue, byte.MaxValue);

    private static double CalculateContrastRatio(Windows.UI.Color foreground, Windows.UI.Color background)
    {
        var foregroundLuminance = CalculateRelativeLuminance(foreground);
        var backgroundLuminance = CalculateRelativeLuminance(background);
        var lighter = Math.Max(foregroundLuminance, backgroundLuminance);
        var darker = Math.Min(foregroundLuminance, backgroundLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double CalculateRelativeLuminance(Windows.UI.Color color)
        => (0.2126 * Linearize(color.R))
            + (0.7152 * Linearize(color.G))
            + (0.0722 * Linearize(color.B));

    private static double Linearize(byte component)
    {
        var normalized = component / 255d;
        return normalized <= 0.04045
            ? normalized / 12.92
            : Math.Pow((normalized + 0.055) / 1.055, 2.4);
    }

    private static bool IsEffectivelyVisible(FrameworkElement element)
    {
        if (element.XamlRoot is null || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return false;
        }

        DependencyObject? current = element;
        while (current is not null)
        {
            if (current is UIElement uiElement
                && (uiElement.Visibility != Visibility.Visible || uiElement.Opacity <= 0.05))
            {
                return false;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return true;
    }

    private static async Task<T?> WaitForDescendantAsync<T>(
        DependencyObject root,
        Func<T, bool> predicate,
        TimeSpan timeout)
        where T : DependencyObject
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (root is UIElement element)
            {
                element.UpdateLayout();
            }

            var match = FindDescendant(root, predicate);
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(100).ConfigureAwait(true);
        }

        return default;
    }

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> predicate)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match && predicate(match))
            {
                return match;
            }

            var nested = FindDescendant(child, predicate);
            if (nested is not null)
            {
                return nested;
            }
        }

        return default;
    }

    private sealed record NumberBoxThemeProbeSnapshot(
        bool Visible,
        bool FocusTransitionSucceeded,
        bool Focused,
        FocusState FocusState,
        ElementTheme NumberBoxTheme,
        ElementTheme InputTheme,
        ElementTheme ContentTheme,
        ElementTheme BorderTheme,
        Windows.UI.Color? Foreground,
        Windows.UI.Color? Background,
        double Contrast,
        int CaptureAttempts,
        bool BoundsSettled,
        string CaptureDiagnostic,
        bool Passed)
    {
        public string Format(int sample)
            => $"NumberBoxThemeProbe: sample={sample} visible={Visible}"
                + $" focusTransition={FocusTransitionSucceeded} focused={Focused}"
                + $" focusState={FocusState} numberBoxTheme={NumberBoxTheme} inputTheme={InputTheme}"
                + $" contentTheme={ContentTheme} borderTheme={BorderTheme}"
                + $" foreground={FormatColor(Foreground)} background={FormatColor(Background)}"
                + $" contrast={Contrast.ToString("F2", CultureInfo.InvariantCulture)}"
                + $" captureAttempts={CaptureAttempts} boundsSettled={BoundsSettled}"
                + $" passed={Passed} captureReason=[{CaptureDiagnostic}]";

        public string FormatUnavailable(int attempt)
            => $"NumberBoxThemeProbe: capture-unavailable attempt={attempt} visible={Visible}"
                + $" focusTransition={FocusTransitionSucceeded} focused={Focused}"
                + $" focusState={FocusState} numberBoxTheme={NumberBoxTheme} inputTheme={InputTheme}"
                + $" contentTheme={ContentTheme} borderTheme={BorderTheme}"
                + $" captureAttempts={CaptureAttempts} boundsSettled={BoundsSettled}"
                + $" captureReason=[{CaptureDiagnostic}]";

        private static string FormatColor(Windows.UI.Color? color)
            => color is { } value
                ? $"#{value.A:X2}{value.R:X2}{value.G:X2}{value.B:X2}"
                : "<unresolved>";
    }

    private sealed record NumberBoxThemeProbeTargets(
        NumberBox NumberBox,
        TextBox InputBox,
        ToggleSwitch FocusSink);

    private readonly record struct RenderedColors(
        Windows.UI.Color? Foreground,
        Windows.UI.Color? Background,
        int Attempts,
        string Diagnostic)
    {
        public static RenderedColors Unavailable { get; } = new(null, null, 0, "template-parts-missing");
    }

    /// <summary>
    /// A rendered background sample, or an unavailable one that names why it could not be read.
    /// </summary>
    private readonly record struct BackgroundCapture(Windows.UI.Color? Color, string Diagnostic)
    {
        public static BackgroundCapture Available(Windows.UI.Color color, string diagnostic)
            => new(color, diagnostic);

        public static BackgroundCapture Unavailable(string diagnostic) => new(null, diagnostic);
    }

    private readonly record struct ColorLayer(
        double Red,
        double Green,
        double Blue,
        double Alpha);

    private sealed record NumberBoxThemeProbeRunResult(
        int CompletedSamples,
        bool ValueUnchanged,
        bool Passed,
        string FailureReason)
    {
        public static NumberBoxThemeProbeRunResult NotStarted { get; }
            = new(0, false, false, "not-started");

        public static NumberBoxThemeProbeRunResult TargetUnavailable { get; }
            = new(0, false, false, "target-unavailable");

        public static NumberBoxThemeProbeRunResult Faulted { get; }
            = new(0, false, false, "exception");
    }
#endif
}
