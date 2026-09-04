namespace SalmonEgg.Presentation.Models.Cli;

/// <summary>
/// How the command's status should read to a user.
/// </summary>
/// <remarks>
/// A presentation concept rather than a UI one: Presentation.Core cannot reference WinUI types, so the
/// mapping onto InfoBarSeverity happens in the view layer's converter. Four levels because the states
/// differ in what a user should do — one is fine, one is informational, one is worth acting on, and one is
/// broken.
/// </remarks>
public enum CliCommandStatusSeverity
{
    Informational,
    Success,
    Warning,
    Error,
}
