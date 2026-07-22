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
}
