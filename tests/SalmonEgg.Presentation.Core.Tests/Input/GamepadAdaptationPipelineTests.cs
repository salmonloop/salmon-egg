using System;
using System.Collections.Generic;
using System.Linq;
using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class GamepadAdaptationPipelineTests
{
    public static IEnumerable<object[]> DirectionalSamples()
    {
        yield return
        [
            Standard(moveUp: true),
            RawController([], [GamepadDirectionalSwitchPosition.Up], []),
            GamepadNavigationIntent.MoveUp
        ];
        yield return
        [
            Standard(moveDown: true),
            RawController([], [GamepadDirectionalSwitchPosition.Down], []),
            GamepadNavigationIntent.MoveDown
        ];
        yield return
        [
            Standard(moveLeft: true),
            RawController([], [GamepadDirectionalSwitchPosition.Left], []),
            GamepadNavigationIntent.MoveLeft
        ];
        yield return
        [
            Standard(moveRight: true),
            RawController([], [GamepadDirectionalSwitchPosition.Right], []),
            GamepadNavigationIntent.MoveRight
        ];
    }

    public static IEnumerable<object[]> ButtonSamples()
    {
        yield return [Standard(activate: true), RawController([RawGameControllerButtonLabel.XboxA], [], []), GamepadNavigationIntent.Activate];
        yield return [Standard(back: true), RawController([RawGameControllerButtonLabel.XboxB], [], []), GamepadNavigationIntent.Back];
        yield return [Standard(activate: true), RawController([RawGameControllerButtonLabel.Cross], [], []), GamepadNavigationIntent.Activate];
        yield return [Standard(back: true), RawController([RawGameControllerButtonLabel.Circle], [], []), GamepadNavigationIntent.Back];
        yield return [Standard(activate: true), RawController([RawGameControllerButtonLabel.LetterB], [], [], RawGameControllerFaceButtonLayout.Nintendo), GamepadNavigationIntent.Activate];
        yield return [Standard(back: true), RawController([RawGameControllerButtonLabel.LetterA], [], [], RawGameControllerFaceButtonLayout.Nintendo), GamepadNavigationIntent.Back];
    }

    public static IEnumerable<object[]> ThumbstickSamples()
    {
        yield return [Thumbstick(0.75, 0.10), RawController([], [], [0.875, 0.45]), GamepadNavigationIntent.MoveRight];
        yield return [Thumbstick(-0.75, 0.10), RawController([], [], [0.125, 0.45]), GamepadNavigationIntent.MoveLeft];
        yield return [Thumbstick(0.10, 0.75), RawController([], [], [0.55, 0.125]), GamepadNavigationIntent.MoveUp];
        yield return [Thumbstick(0.10, -0.75), RawController([], [], [0.55, 0.875]), GamepadNavigationIntent.MoveDown];
    }

    [Theory]
    [MemberData(nameof(DirectionalSamples))]
    public void StandardGamepadAndRawControllerDirections_ProjectToSameNavigationIntent(
        GamepadInputReading standardReading,
        GamepadInputReading rawReading,
        GamepadNavigationIntent expected)
    {
        var standardIntents = GamepadIntentProcessor.GetActiveIntents(standardReading);
        var rawIntents = GamepadIntentProcessor.GetActiveIntents(rawReading);

        Assert.Equal([expected], Order(standardIntents));
        Assert.Equal(Order(standardIntents), Order(rawIntents));
        Assert.Equal(
            Order(standardIntents),
            Order(new GamepadIntentProcessor().Process(standardReading, SampleTime)));
        Assert.Equal(
            Order(standardIntents),
            Order(new GamepadIntentProcessor().Process(rawReading, SampleTime)));
    }

    [Theory]
    [MemberData(nameof(ButtonSamples))]
    public void StandardGamepadAndRawControllerButtons_ProjectToSameNavigationIntent(
        GamepadInputReading standardReading,
        GamepadInputReading rawReading,
        GamepadNavigationIntent expected)
    {
        var standardIntents = GamepadIntentProcessor.GetActiveIntents(standardReading);
        var rawIntents = GamepadIntentProcessor.GetActiveIntents(rawReading);

        Assert.Equal([expected], Order(standardIntents));
        Assert.Equal(Order(standardIntents), Order(rawIntents));
    }

    [Theory]
    [MemberData(nameof(ThumbstickSamples))]
    public void StandardGamepadAndRawControllerThumbsticks_ProjectToSameNavigationIntent(
        GamepadInputReading standardReading,
        GamepadInputReading rawReading,
        GamepadNavigationIntent expected)
    {
        Assert.Equal(standardReading.ThumbstickX, rawReading.ThumbstickX, precision: 10);
        Assert.Equal(standardReading.ThumbstickY, rawReading.ThumbstickY, precision: 10);

        var standardIntents = GamepadIntentProcessor.GetActiveIntents(standardReading);
        var rawIntents = GamepadIntentProcessor.GetActiveIntents(rawReading);

        Assert.Equal([expected], Order(standardIntents));
        Assert.Equal(Order(standardIntents), Order(rawIntents));
    }

    [Fact]
    public void RawFallbackReading_FansOutThroughAllCommonProcessorsWithoutCrossSuppressing()
    {
        var reading = Standard(
            moveRight: true,
            activate: true,
            shortcutVoiceToggle: true,
            leftTrigger: 0.75);
        var selected = GamepadActiveReadingSelector.TrySelectActiveReading(
            [default],
            [reading],
            out var selection);
        var navigationProcessor = new GamepadIntentProcessor();
        var shortcutProcessor = new GamepadShortcutProcessor();
        var contextProcessor = new GamepadContextIntentProcessor();

        Assert.True(selected);
        Assert.Equal(GamepadInputPath.RawGameController, selection.InputPath);
        Assert.Equal(
            [GamepadNavigationIntent.MoveRight, GamepadNavigationIntent.Activate],
            Order(navigationProcessor.Process(selection.Reading, SampleTime)));
        Assert.Equal([GamepadShortcutIntent.ToggleVoiceInput], shortcutProcessor.Process(selection.Reading));
        Assert.Equal([GamepadContextIntent.PageUp], contextProcessor.Process(selection.Reading));

        Assert.Empty(navigationProcessor.Process(selection.Reading, SampleTime.AddMilliseconds(10)));
        Assert.Empty(shortcutProcessor.Process(selection.Reading));
        Assert.Empty(contextProcessor.Process(selection.Reading));

        _ = navigationProcessor.Process(default, SampleTime.AddMilliseconds(20));
        _ = shortcutProcessor.Process(default);
        _ = contextProcessor.Process(default);

        Assert.Equal(
            [GamepadNavigationIntent.MoveRight, GamepadNavigationIntent.Activate],
            Order(navigationProcessor.Process(selection.Reading, SampleTime.AddMilliseconds(30))));
        Assert.Equal([GamepadShortcutIntent.ToggleVoiceInput], shortcutProcessor.Process(selection.Reading));
        Assert.Equal([GamepadContextIntent.PageUp], contextProcessor.Process(selection.Reading));
    }

    [Fact]
    public void ShortcutAndContextOnlyReadings_AreSelectableWithoutBecomingNavigation()
    {
        var shortcutOnly = Standard(shortcutVoiceToggle: true);
        var contextOnly = Standard(rightTrigger: 0.75);

        Assert.Empty(GamepadIntentProcessor.GetActiveIntents(shortcutOnly));
        Assert.Empty(GamepadIntentProcessor.GetActiveIntents(contextOnly));
        Assert.True(GamepadActiveReadingSelector.TrySelectActiveReading([shortcutOnly], [], out var shortcutSelection));
        Assert.True(GamepadActiveReadingSelector.TrySelectActiveReading([], [contextOnly], out var contextSelection));
        Assert.Equal(GamepadInputPath.Gamepad, shortcutSelection.InputPath);
        Assert.Equal(GamepadInputPath.RawGameController, contextSelection.InputPath);
    }

    [Fact]
    public void StandardGamepadAndRawControllerVoiceShortcut_ProjectToSameShortcutIntent()
    {
        var standardReading = Standard(shortcutVoiceToggle: true);
        var rawReading = RawController([RawGameControllerButtonLabel.XboxY], [], []);

        Assert.Empty(GamepadIntentProcessor.GetActiveIntents(standardReading));
        Assert.Empty(GamepadIntentProcessor.GetActiveIntents(rawReading));
        Assert.Equal(
            GamepadShortcutIntentProjector.GetActiveShortcuts(standardReading),
            GamepadShortcutIntentProjector.GetActiveShortcuts(rawReading));
    }

    [Fact]
    public void StandardGamepadAndNintendoRawControllerVoiceShortcut_ProjectToSameShortcutIntent()
    {
        var standardReading = Standard(shortcutVoiceToggle: true);
        var rawReading = RawController(
            [RawGameControllerButtonLabel.LetterX],
            [],
            [],
            RawGameControllerFaceButtonLayout.Nintendo);

        Assert.Empty(GamepadIntentProcessor.GetActiveIntents(standardReading));
        Assert.Empty(GamepadIntentProcessor.GetActiveIntents(rawReading));
        Assert.Equal(
            GamepadShortcutIntentProjector.GetActiveShortcuts(standardReading),
            GamepadShortcutIntentProjector.GetActiveShortcuts(rawReading));
    }

    [Fact]
    public void NintendoRawControllerReading_SelectedFromRawPath_FansOutThroughCommonProcessors()
    {
        var rawReading = RawController(
            [RawGameControllerButtonLabel.LetterB, RawGameControllerButtonLabel.LetterX],
            [],
            [],
            RawGameControllerFaceButtonLayout.Nintendo);
        var selected = GamepadActiveReadingSelector.TrySelectActiveReading(
            [default],
            [rawReading],
            out var selection);

        Assert.True(selected);
        Assert.Equal(GamepadInputPath.RawGameController, selection.InputPath);
        Assert.Equal(
            [GamepadNavigationIntent.Activate],
            Order(new GamepadIntentProcessor().Process(selection.Reading, SampleTime)));
        Assert.Equal(
            [GamepadShortcutIntent.ToggleVoiceInput],
            new GamepadShortcutProcessor().Process(selection.Reading));
        Assert.Empty(GamepadContextIntentProjector.GetActiveIntents(selection.Reading));
    }

    [Fact]
    public void StandardGamepadAndRawControllerTriggers_ProjectToSameContextIntent()
    {
        var standardReading = Standard(leftTrigger: 0.75, rightTrigger: 0.75);
        var rawReading = RawController(
            [RawGameControllerButtonLabel.XboxLeftTrigger, RawGameControllerButtonLabel.XboxRightTrigger],
            [],
            []);

        Assert.Empty(GamepadIntentProcessor.GetActiveIntents(standardReading));
        Assert.Empty(GamepadIntentProcessor.GetActiveIntents(rawReading));
        Assert.Equal(
            GamepadContextIntentProjector.GetActiveIntents(standardReading),
            GamepadContextIntentProjector.GetActiveIntents(rawReading));
    }


    [Fact]
    public void XboxPlayStationAndNintendoStandardLabels_ProjectExpectedFaceSemantics()
    {
        var xbox = LabeledStandard(
            faceA: true,
            faceB: true,
            faceX: true,
            faceY: true,
            labels: new(
                A: RawGameControllerButtonLabel.XboxA,
                B: RawGameControllerButtonLabel.XboxB,
                X: RawGameControllerButtonLabel.XboxX,
                Y: RawGameControllerButtonLabel.XboxY));
        var playstation = LabeledStandard(
            faceA: true,
            faceB: true,
            faceX: true,
            faceY: true,
            labels: new(
                A: RawGameControllerButtonLabel.Cross,
                B: RawGameControllerButtonLabel.Circle,
                X: RawGameControllerButtonLabel.Square,
                Y: RawGameControllerButtonLabel.Triangle));
        var nintendoPositionNormalized = LabeledStandard(
            faceA: true,
            faceB: true,
            faceX: true,
            faceY: true,
            labels: new(
                A: RawGameControllerButtonLabel.LetterB,
                B: RawGameControllerButtonLabel.LetterA,
                X: RawGameControllerButtonLabel.LetterY,
                Y: RawGameControllerButtonLabel.LetterX));

        Assert.Equal(
            [GamepadNavigationIntent.Activate, GamepadNavigationIntent.Back],
            Order(GamepadIntentProcessor.GetActiveIntents(xbox)));
        Assert.Equal([GamepadShortcutIntent.ToggleVoiceInput], GamepadShortcutIntentProjector.GetActiveShortcuts(xbox));

        Assert.Equal(
            [GamepadNavigationIntent.Activate, GamepadNavigationIntent.Back],
            Order(GamepadIntentProcessor.GetActiveIntents(playstation)));
        Assert.Equal([GamepadShortcutIntent.ToggleVoiceInput], GamepadShortcutIntentProjector.GetActiveShortcuts(playstation));

        Assert.Equal(
            [GamepadNavigationIntent.Activate, GamepadNavigationIntent.Back],
            Order(GamepadIntentProcessor.GetActiveIntents(nintendoPositionNormalized)));
        Assert.Equal(
            [GamepadShortcutIntent.ToggleVoiceInput],
            GamepadShortcutIntentProjector.GetActiveShortcuts(nintendoPositionNormalized));
    }

    [Fact]
    public void DualExposedSwitchController_StandardAndRawPathsProjectSamePhysicalFaceSemantics()
    {
        // Standard path: Windows position slots with physical Letter* labels.
        var standardBottom = LabeledStandard(
            faceA: true,
            labels: new(A: RawGameControllerButtonLabel.LetterB));
        var standardEast = LabeledStandard(
            faceB: true,
            labels: new(B: RawGameControllerButtonLabel.LetterA));
        var standardNorth = LabeledStandard(
            faceY: true,
            labels: new(Y: RawGameControllerButtonLabel.LetterX));
        var standardWest = LabeledStandard(
            faceX: true,
            labels: new(X: RawGameControllerButtonLabel.LetterY));

        // Raw path: identity Nintendo + physical letter presses (or letter-only promotion).
        var rawBottom = RawController([RawGameControllerButtonLabel.LetterB], [], [], RawGameControllerFaceButtonLayout.Nintendo);
        var rawEast = RawController([RawGameControllerButtonLabel.LetterA], [], [], RawGameControllerFaceButtonLayout.Nintendo);
        var rawNorth = RawController([RawGameControllerButtonLabel.LetterX], [], [], RawGameControllerFaceButtonLayout.Nintendo);
        var rawWest = RawController([RawGameControllerButtonLabel.LetterY], [], [], RawGameControllerFaceButtonLayout.Nintendo);

        Assert.Equal(
            Order(GamepadIntentProcessor.GetActiveIntents(standardBottom)),
            Order(GamepadIntentProcessor.GetActiveIntents(rawBottom)));
        Assert.Equal(
            Order(GamepadIntentProcessor.GetActiveIntents(standardEast)),
            Order(GamepadIntentProcessor.GetActiveIntents(rawEast)));
        Assert.Equal(
            GamepadShortcutIntentProjector.GetActiveShortcuts(standardNorth),
            GamepadShortcutIntentProjector.GetActiveShortcuts(rawNorth));
        Assert.Empty(GamepadIntentProcessor.GetActiveIntents(standardWest));
        Assert.Empty(GamepadShortcutIntentProjector.GetActiveShortcuts(standardWest));
        Assert.Empty(GamepadIntentProcessor.GetActiveIntents(rawWest));
        Assert.Empty(GamepadShortcutIntentProjector.GetActiveShortcuts(rawWest));

        Assert.True(GamepadActiveReadingSelector.TrySelectActiveReading(
            [standardBottom],
            [rawEast],
            out var selection));
        Assert.Equal(GamepadInputPath.Gamepad, selection.InputPath);
        Assert.Equal([GamepadNavigationIntent.Activate], Order(GamepadIntentProcessor.GetActiveIntents(selection.Reading)));
    }

    [Theory]
    [InlineData(RawGameControllerButtonLabel.XboxA, true, false, false)]
    [InlineData(RawGameControllerButtonLabel.XboxB, false, true, false)]
    [InlineData(RawGameControllerButtonLabel.XboxX, false, false, false)]
    [InlineData(RawGameControllerButtonLabel.XboxY, false, false, true)]
    [InlineData(RawGameControllerButtonLabel.Cross, true, false, false)]
    [InlineData(RawGameControllerButtonLabel.Circle, false, true, false)]
    [InlineData(RawGameControllerButtonLabel.Square, false, false, false)]
    [InlineData(RawGameControllerButtonLabel.Triangle, false, false, true)]
    public void LabeledRawFaceButtons_ProjectXboxAndPlayStationPhysicalSemantics(
        RawGameControllerButtonLabel label,
        bool activate,
        bool back,
        bool voice)
    {
        var reading = RawController([label], [], [], RawGameControllerFaceButtonLayout.Standard);

        Assert.Equal(activate, reading.Activate);
        Assert.Equal(back, reading.Back);
        Assert.Equal(voice, reading.ShortcutVoiceToggle);

        var expectedNav = new List<GamepadNavigationIntent>();
        if (activate)
        {
            expectedNav.Add(GamepadNavigationIntent.Activate);
        }

        if (back)
        {
            expectedNav.Add(GamepadNavigationIntent.Back);
        }

        Assert.Equal(expectedNav, Order(GamepadIntentProcessor.GetActiveIntents(reading)));
        Assert.Equal(
            voice ? [GamepadShortcutIntent.ToggleVoiceInput] : Array.Empty<GamepadShortcutIntent>(),
            GamepadShortcutIntentProjector.GetActiveShortcuts(reading));
    }

    [Theory]
    [InlineData(RawGameControllerButtonLabel.LetterB, true, false, false)]
    [InlineData(RawGameControllerButtonLabel.LetterA, false, true, false)]
    [InlineData(RawGameControllerButtonLabel.LetterY, false, false, false)]
    [InlineData(RawGameControllerButtonLabel.LetterX, false, false, true)]
    public void LabeledRawNintendoFaceButtons_ProjectPhysicalGlyphSemantics(
        RawGameControllerButtonLabel label,
        bool activate,
        bool back,
        bool voice)
    {
        var reading = RawController([label], [], [], RawGameControllerFaceButtonLayout.Nintendo);

        Assert.Equal(activate, reading.Activate);
        Assert.Equal(back, reading.Back);
        Assert.Equal(voice, reading.ShortcutVoiceToggle);

        var expectedNav = new List<GamepadNavigationIntent>();
        if (activate)
        {
            expectedNav.Add(GamepadNavigationIntent.Activate);
        }

        if (back)
        {
            expectedNav.Add(GamepadNavigationIntent.Back);
        }

        Assert.Equal(expectedNav, Order(GamepadIntentProcessor.GetActiveIntents(reading)));
        Assert.Equal(
            voice ? [GamepadShortcutIntent.ToggleVoiceInput] : Array.Empty<GamepadShortcutIntent>(),
            GamepadShortcutIntentProjector.GetActiveShortcuts(reading));
    }

    [Theory]
    [InlineData("Joy-Con (L)", (ushort)0x057E)]
    [InlineData("Joy-Con (R)", (ushort)0x057E)]
    [InlineData("JoyCon (L)", (ushort)0x0000)]
    public void SingleJoyCon_RejectsFullGamepadUnlabeledIndexFallback(string displayName, ushort vendorId)
    {
        Assert.False(RawGameControllerUnlabeledFaceIndexPolicy.SupportsFullGamepadUnlabeledIndexFallback(
            displayName,
            vendorId));

        var reading = RawGameControllerInputReadingMapper.GetInputReadingFromPresses(
            [
                new RawGameControllerButtonPress(0, RawGameControllerButtonLabel.None),
                new RawGameControllerButtonPress(1, RawGameControllerButtonLabel.None),
                new RawGameControllerButtonPress(6, RawGameControllerButtonLabel.None)
            ],
            [],
            [],
            RawGameControllerFaceButtonLayoutResolver.Resolve(displayName, vendorId),
            allowUnlabeledFaceIndexFallback: false);

        Assert.Equal(default, reading);
    }

    [Fact]
    public void UnlabeledXboxWestFaceAndTriggers_MapNoOpAndPageContext()
    {
        // Xbox/Nintendo full pads: index 2 is west face (no-op); 6/7 are digital triggers.
        var reading = RawGameControllerInputReadingMapper.GetInputReadingFromPresses(
            [
                new RawGameControllerButtonPress(2, RawGameControllerButtonLabel.None),
                new RawGameControllerButtonPress(6, RawGameControllerButtonLabel.None),
                new RawGameControllerButtonPress(7, RawGameControllerButtonLabel.None)
            ],
            [],
            [],
            RawGameControllerFaceButtonLayout.Standard,
            allowUnlabeledFaceIndexFallback: true,
            displayName: "Xbox Wireless Controller",
            hardwareVendorId: 0x045E);

        Assert.False(reading.Activate);
        Assert.False(reading.Back);
        Assert.False(reading.ShortcutVoiceToggle);
        Assert.Empty(GamepadIntentProcessor.GetActiveIntents(reading));
        Assert.Empty(GamepadShortcutIntentProjector.GetActiveShortcuts(reading));
        Assert.Equal(
            [GamepadContextIntent.PageUp, GamepadContextIntent.PageDown],
            GamepadContextIntentProjector.GetActiveIntents(reading).OrderBy(static intent => intent));
    }

    [Fact]
    public void UnlabeledSonySquareAndTriggers_MapNoOpAndPageContext()
    {
        // DualSense HID: index 0 is Square (west no-op); index 2 is Circle (Back).
        var reading = RawGameControllerInputReadingMapper.GetInputReadingFromPresses(
            [
                new RawGameControllerButtonPress(0, RawGameControllerButtonLabel.None),
                new RawGameControllerButtonPress(6, RawGameControllerButtonLabel.None),
                new RawGameControllerButtonPress(7, RawGameControllerButtonLabel.None)
            ],
            [],
            [],
            RawGameControllerFaceButtonLayout.Standard,
            allowUnlabeledFaceIndexFallback: true,
            displayName: "DualSense Wireless Controller",
            hardwareVendorId: 0x054C);

        Assert.False(reading.Activate);
        Assert.False(reading.Back);
        Assert.False(reading.ShortcutVoiceToggle);
        Assert.Empty(GamepadIntentProcessor.GetActiveIntents(reading));
        Assert.Empty(GamepadShortcutIntentProjector.GetActiveShortcuts(reading));
        Assert.Equal(
            [GamepadContextIntent.PageUp, GamepadContextIntent.PageDown],
            GamepadContextIntentProjector.GetActiveIntents(reading).OrderBy(static intent => intent));
    }

    [Fact]
    public void RawLetterLabelsWithoutNintendoIdentity_StillUseNintendoFaceSemantics()
    {
        var reading = RawController(
            [RawGameControllerButtonLabel.LetterB, RawGameControllerButtonLabel.LetterX],
            [],
            [],
            RawGameControllerFaceButtonLayout.Standard);

        Assert.Equal([GamepadNavigationIntent.Activate], Order(GamepadIntentProcessor.GetActiveIntents(reading)));
        Assert.Equal([GamepadShortcutIntent.ToggleVoiceInput], GamepadShortcutIntentProjector.GetActiveShortcuts(reading));
    }


    [Theory]
    [InlineData(0x045E, "Xbox Wireless Controller")]
    [InlineData(0x054C, "Wireless Controller")]
    [InlineData(0x057E, "Nintendo Switch Pro Controller")]
    public void UnlabeledKnownFamilyFaceIndexes_ProjectThroughRawPathLikeLabeledStandardFaces(
        ushort vendorId,
        string displayName)
    {
        Assert.True(RawGameControllerUnlabeledFaceIndexPolicy.SupportsFallback(displayName, vendorId));

        var reading = RawGameControllerInputReadingMapper.GetInputReadingFromPresses(
            [
                new RawGameControllerButtonPress(0, RawGameControllerButtonLabel.None),
                new RawGameControllerButtonPress(1, RawGameControllerButtonLabel.None),
                new RawGameControllerButtonPress(2, RawGameControllerButtonLabel.None),
                new RawGameControllerButtonPress(3, RawGameControllerButtonLabel.None)
            ],
            [],
            [],
            RawGameControllerFaceButtonLayoutResolver.Resolve(displayName, vendorId),
            allowUnlabeledFaceIndexFallback: true,
            displayName: displayName,
            hardwareVendorId: vendorId);

        Assert.Equal(
            [GamepadNavigationIntent.Activate, GamepadNavigationIntent.Back],
            Order(GamepadIntentProcessor.GetActiveIntents(reading)));
        Assert.Equal([GamepadShortcutIntent.ToggleVoiceInput], GamepadShortcutIntentProjector.GetActiveShortcuts(reading));
    }

    [Fact]
    public void UnlabeledSonyHidFaceIndexes_ProjectCrossActivateAndSquareNoOp()
    {
        var cross = RawGameControllerInputReadingMapper.GetInputReadingFromPresses(
            [new RawGameControllerButtonPress(1, RawGameControllerButtonLabel.None)],
            [],
            [],
            RawGameControllerFaceButtonLayout.Standard,
            allowUnlabeledFaceIndexFallback: true,
            displayName: "Wireless Controller",
            hardwareVendorId: 0x054C);
        var square = RawGameControllerInputReadingMapper.GetInputReadingFromPresses(
            [new RawGameControllerButtonPress(0, RawGameControllerButtonLabel.None)],
            [],
            [],
            RawGameControllerFaceButtonLayout.Standard,
            allowUnlabeledFaceIndexFallback: true,
            displayName: "DualSense Wireless Controller",
            hardwareVendorId: 0x054C);
        var circle = RawGameControllerInputReadingMapper.GetInputReadingFromPresses(
            [new RawGameControllerButtonPress(2, RawGameControllerButtonLabel.None)],
            [],
            [],
            RawGameControllerFaceButtonLayout.Standard,
            allowUnlabeledFaceIndexFallback: true,
            displayName: "DualSense Wireless Controller",
            hardwareVendorId: 0x054C);
        var triangle = RawGameControllerInputReadingMapper.GetInputReadingFromPresses(
            [new RawGameControllerButtonPress(3, RawGameControllerButtonLabel.None)],
            [],
            [],
            RawGameControllerFaceButtonLayout.Standard,
            allowUnlabeledFaceIndexFallback: true,
            displayName: "DualSense Wireless Controller",
            hardwareVendorId: 0x054C);

        Assert.Equal([GamepadNavigationIntent.Activate], Order(GamepadIntentProcessor.GetActiveIntents(cross)));
        Assert.Empty(GamepadIntentProcessor.GetActiveIntents(square));
        Assert.Empty(GamepadShortcutIntentProjector.GetActiveShortcuts(square));
        Assert.Equal([GamepadNavigationIntent.Back], Order(GamepadIntentProcessor.GetActiveIntents(circle)));
        Assert.Equal([GamepadShortcutIntent.ToggleVoiceInput], GamepadShortcutIntentProjector.GetActiveShortcuts(triangle));
    }

    [Fact]
    public void MultiBrandTriggerTravel_ProjectsSamePageIntentsOnStandardAndRawPaths()
    {
        var standardLeft = Standard(leftTrigger: 0.75);
        var standardRight = Standard(rightTrigger: 1.0);

        // Xbox / DualSense raw-only analog slots 4/5 (sticks centered so only triggers matter).
        var xboxLeft = RawAnalogTriggers(
            displayName: "Xbox Wireless Controller",
            vendorId: 0x045E,
            left: 0.75,
            right: 0);
        var dualSenseRight = RawAnalogTriggers(
            displayName: "DualSense Wireless Controller",
            vendorId: 0x054C,
            left: 0,
            right: 1.0);
        // Switch Pro: digital L2/R2 only — unlabeled B6/B7, not axes 4/5.
        var switchLeft = RawGameControllerInputReadingMapper.GetInputReadingFromPresses(
            [new RawGameControllerButtonPress(6, RawGameControllerButtonLabel.None)],
            [],
            [0.5, 0.5, 0.5, 0.5, 1.0, 1.0],
            RawGameControllerFaceButtonLayout.Nintendo,
            allowUnlabeledFaceIndexFallback: true,
            displayName: "Pro Controller",
            hardwareVendorId: 0x057E);
        var switchRight = RawGameControllerInputReadingMapper.GetInputReadingFromPresses(
            [new RawGameControllerButtonPress(7, RawGameControllerButtonLabel.None)],
            [],
            [0.5, 0.5, 0.5, 0.5, 1.0, 1.0],
            RawGameControllerFaceButtonLayout.Nintendo,
            allowUnlabeledFaceIndexFallback: true,
            displayName: "Pro Controller",
            hardwareVendorId: 0x057E);

        Assert.Equal(
            GamepadContextIntentProjector.GetActiveIntents(standardLeft).OrderBy(static i => i),
            GamepadContextIntentProjector.GetActiveIntents(xboxLeft).OrderBy(static i => i));
        Assert.Equal(
            GamepadContextIntentProjector.GetActiveIntents(standardRight).OrderBy(static i => i),
            GamepadContextIntentProjector.GetActiveIntents(dualSenseRight).OrderBy(static i => i));
        Assert.Equal([GamepadContextIntent.PageUp], GamepadContextIntentProjector.GetActiveIntents(switchLeft));
        Assert.Equal([GamepadContextIntent.PageDown], GamepadContextIntentProjector.GetActiveIntents(switchRight));
        // Nintendo must not treat full axes 4/5 as analog LT/RT when only digital L2 is pressed.
        Assert.Equal(1.0, switchLeft.LeftTrigger);
        Assert.Equal(0.0, switchLeft.RightTrigger);
    }

    [Theory]
    [InlineData("Xbox Wireless Controller", (ushort)0x045E, "XboxA", "XboxB", "XboxX", "XboxY")]
    [InlineData("DualSense Wireless Controller", (ushort)0x054C, "Cross", "Circle", "Square", "Triangle")]
    [InlineData("Pro Controller", (ushort)0x057E, "LetterB", "LetterA", "LetterY", "LetterX")]
    public void MultiBrandLabeledFaceButtons_ProjectSharedPhysicalFaceSemantics(
        string displayName,
        ushort vendorId,
        string activateLabel,
        string backLabel,
        string westLabel,
        string voiceLabel)
    {
        var layout = RawGameControllerFaceButtonLayoutResolver.Resolve(displayName, vendorId);
        var activate = RawController([ParseLabel(activateLabel)], [], [], layout);
        var back = RawController([ParseLabel(backLabel)], [], [], layout);
        var west = RawController([ParseLabel(westLabel)], [], [], layout);
        var voice = RawController([ParseLabel(voiceLabel)], [], [], layout);

        Assert.Equal([GamepadNavigationIntent.Activate], Order(GamepadIntentProcessor.GetActiveIntents(activate)));
        Assert.Equal([GamepadNavigationIntent.Back], Order(GamepadIntentProcessor.GetActiveIntents(back)));
        Assert.Empty(GamepadIntentProcessor.GetActiveIntents(west));
        Assert.Empty(GamepadShortcutIntentProjector.GetActiveShortcuts(west));
        Assert.Equal([GamepadShortcutIntent.ToggleVoiceInput], GamepadShortcutIntentProjector.GetActiveShortcuts(voice));
    }

    private static readonly DateTimeOffset SampleTime = DateTimeOffset.Parse("2026-07-06T00:00:00Z");

    private static GamepadInputReading LabeledStandard(
        bool faceA = false,
        bool faceB = false,
        bool faceX = false,
        bool faceY = false,
        StandardGamepadFaceButtonLabels labels = default)
        => StandardGamepadInputReadingMapper.GetInputReading(
            moveUp: false,
            moveDown: false,
            moveLeft: false,
            moveRight: false,
            faceAPressed: faceA,
            faceBPressed: faceB,
            faceXPressed: faceX,
            faceYPressed: faceY,
            leftTrigger: 0,
            rightTrigger: 0,
            thumbstickX: 0,
            thumbstickY: 0,
            labels: labels);

    private static GamepadInputReading Standard(
        bool moveUp = false,
        bool moveDown = false,
        bool moveLeft = false,
        bool moveRight = false,
        bool activate = false,
        bool back = false,
        bool shortcutVoiceToggle = false,
        double leftTrigger = 0,
        double rightTrigger = 0)
        => new(
            MoveUp: moveUp,
            MoveDown: moveDown,
            MoveLeft: moveLeft,
            MoveRight: moveRight,
            Activate: activate,
            Back: back,
            ShortcutVoiceToggle: shortcutVoiceToggle,
            LeftTrigger: leftTrigger,
            RightTrigger: rightTrigger);

    private static GamepadInputReading Thumbstick(double x, double y)
        => new(
            MoveUp: false,
            MoveDown: false,
            MoveLeft: false,
            MoveRight: false,
            Activate: false,
            Back: false,
            ThumbstickX: x,
            ThumbstickY: y);

    private static GamepadNavigationIntent[] Order(IEnumerable<GamepadNavigationIntent> intents)
        => intents.OrderBy(static intent => intent).ToArray();


    private static GamepadInputReading RawAnalogTriggers(
        string displayName,
        ushort vendorId,
        double left,
        double right)
        => RawGameControllerInputReadingMapper.GetInputReadingFromPresses(
            [],
            [],
            [0.5, 0.5, 0.5, 0.5, left, right],
            RawGameControllerFaceButtonLayout.Standard,
            allowUnlabeledFaceIndexFallback: true,
            displayName: displayName,
            hardwareVendorId: vendorId);

    private static RawGameControllerButtonLabel ParseLabel(string name)
        => Enum.Parse<RawGameControllerButtonLabel>(name);

    private static GamepadInputReading RawController(
        IReadOnlyList<RawGameControllerButtonLabel> pressedButtonLabels,
        IReadOnlyList<GamepadDirectionalSwitchPosition> switches,
        IReadOnlyList<double> axes,
        RawGameControllerFaceButtonLayout faceButtonLayout = RawGameControllerFaceButtonLayout.Standard)
        => RawGameControllerInputReadingMapper.GetInputReading(
            pressedButtonLabels,
            switches,
            axes,
            faceButtonLayout);

    [Fact]
    public void SonyCrossCircleTriangleSquare_MatchAppFaceContractAcrossLabeledRawPath()
    {
        // Labeled DualSense glyphs must share app semantics with standard Activate/Back/Voice/west-no-op.
        var activate = RawController([RawGameControllerButtonLabel.Cross], [], []);
        var back = RawController([RawGameControllerButtonLabel.Circle], [], []);
        var voice = RawController([RawGameControllerButtonLabel.Triangle], [], []);
        var west = RawController([RawGameControllerButtonLabel.Square], [], []);

        Assert.Equal([GamepadNavigationIntent.Activate], Order(GamepadIntentProcessor.GetActiveIntents(activate)));
        Assert.Equal([GamepadNavigationIntent.Back], Order(GamepadIntentProcessor.GetActiveIntents(back)));
        Assert.Equal([GamepadShortcutIntent.ToggleVoiceInput], GamepadShortcutIntentProjector.GetActiveShortcuts(voice).OrderBy(static x => x));
        Assert.Empty(GamepadIntentProcessor.GetActiveIntents(west));
        Assert.Empty(GamepadShortcutIntentProjector.GetActiveShortcuts(west));
    }

    [Fact]
    public void NintendoPhysicalLetters_MatchXboxAppFaceContractAtPhysicalPositions()
    {
        var activate = RawController(
            [RawGameControllerButtonLabel.LetterB],
            [],
            [],
            RawGameControllerFaceButtonLayout.Nintendo);
        var back = RawController(
            [RawGameControllerButtonLabel.LetterA],
            [],
            [],
            RawGameControllerFaceButtonLayout.Nintendo);
        var voice = RawController(
            [RawGameControllerButtonLabel.LetterX],
            [],
            [],
            RawGameControllerFaceButtonLayout.Nintendo);
        var west = RawController(
            [RawGameControllerButtonLabel.LetterY],
            [],
            [],
            RawGameControllerFaceButtonLayout.Nintendo);

        Assert.Equal([GamepadNavigationIntent.Activate], Order(GamepadIntentProcessor.GetActiveIntents(activate)));
        Assert.Equal([GamepadNavigationIntent.Back], Order(GamepadIntentProcessor.GetActiveIntents(back)));
        Assert.Equal([GamepadShortcutIntent.ToggleVoiceInput], GamepadShortcutIntentProjector.GetActiveShortcuts(voice).OrderBy(static x => x));
        Assert.Empty(GamepadIntentProcessor.GetActiveIntents(west));
        Assert.Empty(GamepadShortcutIntentProjector.GetActiveShortcuts(west));
    }

}
