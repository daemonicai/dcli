using Dcli.Demo.DmonWizard.Steps;

namespace Dcli.Demo.DmonWizard.Engine;

/// <summary>
/// Renders wizard steps using dcli's public dialog API.
/// Replaces Spectre.Console + InlinePrompt from Dmon.Terminal.WizardRenderer.
/// </summary>
internal static class WizardRenderer
{
    public static Task<WizardStepOutcome> RenderAsync(
        ITerminal terminal, WizardStep step, CancellationToken ct)
    {
        return step switch
        {
            ChooseOneStep s => RenderChooseOneAsync(terminal, s, ct),
            ChooseManyStep s => RenderChooseManyAsync(terminal, s, ct),
            TextInputStep s => RenderTextInputAsync(terminal, s, ct),
            YesNoStep s => RenderYesNoAsync(terminal, s, ct),
            InfoStep s => RenderInfoAsync(terminal, s, ct),
            _ => Task.FromResult(WizardStepOutcome.Cancel),
        };
    }

    private static async Task<WizardStepOutcome> RenderChooseOneAsync(
        ITerminal terminal, ChooseOneStep step, CancellationToken ct)
    {
        List<Line> items = step.Options
            .Select(o => Line.FromText(o.Label))
            .ToList();

        // AllowBack: true — Backspace at the top of the list produces DialogOutcome.Back,
        // routing the user to the previous wizard step.
        DialogResult<int> result = await terminal.SelectAsync(
            new SelectRequest(
                Items: items,
                Title: Line.Bold(step.Prompt),
                AllowBack: true),
            ct).ConfigureAwait(false);

        if (result.Outcome == DialogOutcome.Back)
            return WizardStepOutcome.Back;

        if (result.Outcome == DialogOutcome.Cancelled)
            return WizardStepOutcome.Cancel;

        step.SelectedIndex = result.Value;
        return WizardStepOutcome.Answered;
    }

    private static async Task<WizardStepOutcome> RenderChooseManyAsync(
        ITerminal terminal, ChooseManyStep step, CancellationToken ct)
    {
        // Ergonomics WIN: dcli MultiSelectAsync supports real multi-select.
        // Dmon fell back to single-pick because Spectre.Console couldn't multi-select cleanly.
        List<Line> items = step.Options
            .Select(o => Line.FromText(o.Label))
            .ToList();

        DialogResult<int[]> result = await terminal.MultiSelectAsync(
            new MultiSelectRequest(
                Items: items,
                Title: Line.Bold(step.Prompt)),
            ct).ConfigureAwait(false);

        if (result.Outcome == DialogOutcome.Cancelled)
            return WizardStepOutcome.Cancel;

        step.SelectedIndices = result.Value.ToList();
        return WizardStepOutcome.Answered;
    }

    private static async Task<WizardStepOutcome> RenderTextInputAsync(
        ITerminal terminal, TextInputStep step, CancellationToken ct)
    {
        if (step.Default is not null)
        {
            string shown = step.Secret ? new string('*', 8) : step.Default;
            terminal.Scrollback.Append(Line.Dim($"Default: {shown}"));
        }

        IReadOnlyList<Line> prompt = step.Secret
            ? (IReadOnlyList<Line>)
            [
                Line.Bold(step.Prompt),
                Line.Dim("Used only for this session. Not persisted to disk."),
                Line.Dim("Press Esc to cancel; Enter to confirm."),
            ]
            : [Line.Bold(step.Prompt)];

        DialogResult<string> result = await terminal.InputAsync(
            new InputRequest(
                Prompt: prompt,
                Default: step.Default,
                IsSecret: step.Secret),
            ct).ConfigureAwait(false);

        if (result.Outcome == DialogOutcome.Cancelled)
            return WizardStepOutcome.Cancel;

        string value = result.Value ?? string.Empty;

        if (value.Length == 0 && step.Default is not null)
            value = step.Default;

        if (value.Length == 0 && step.Required)
            return WizardStepOutcome.Cancel;

        step.Value = value.Length > 0 ? value : null;
        return WizardStepOutcome.Answered;
    }

    private static async Task<WizardStepOutcome> RenderYesNoAsync(
        ITerminal terminal, YesNoStep step, CancellationToken ct)
    {
        // Use ChoiceAsync for a clean Yes/No prompt.
        // Honour step.Default by placing the default option first.
        List<Line> options = step.Default
            ? [Line.Fg("Yes", Color.Named(Color.AnsiColor.Green)),
               Line.Fg("No",  Color.Named(Color.AnsiColor.Red))]
            : [Line.Fg("No",  Color.Named(Color.AnsiColor.Red)),
               Line.Fg("Yes", Color.Named(Color.AnsiColor.Green))];

        string hint = step.Default ? "[Y/n]" : "[y/N]";

        DialogResult<int> result = await terminal.ChoiceAsync(
            new ChoiceRequest(
                Options: options,
                Prompt: Line.Bold($"{step.Prompt} {hint}")),
            ct).ConfigureAwait(false);

        if (result.Outcome == DialogOutcome.Cancelled)
            return WizardStepOutcome.Cancel;

        // When default=true, index 0 = Yes; when default=false, index 0 = No.
        bool selectedYes = step.Default ? result.Value == 0 : result.Value == 1;
        step.Answer = selectedYes;
        return WizardStepOutcome.Answered;
    }

    private static Task<WizardStepOutcome> RenderInfoAsync(
        ITerminal terminal, InfoStep step, CancellationToken ct)
    {
        terminal.Scrollback.Append(Line.Dim(step.Prompt));
        return Task.FromResult(WizardStepOutcome.Answered);
    }
}
