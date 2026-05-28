using Dcli.Demo.DmonWizard.Providers;
using Dcli.Demo.DmonWizard.Steps;

namespace Dcli.Demo.DmonWizard.Engine;

/// <summary>
/// Drives the provider setup wizard: presents provider selection, loops on
/// IProviderFactory.GetNextStepAsync, and returns the result on success.
///
/// The back-stack logic and step-routing are byte-for-byte identical to Dmon.Terminal.WizardEngine.
/// Only the rendering dependency changes: the Spectre.Console + InlinePrompt renderer is replaced
/// by WizardRenderer.RenderAsync(ITerminal, step, ct), and AnsiConsole.MarkupLine is replaced
/// by IScrollback.Append.
/// </summary>
internal sealed class WizardEngine
{
    private readonly ITerminal _terminal;
    private readonly IReadOnlyList<IProviderFactory> _factories;
    private readonly Func<WizardStep, CancellationToken, Task<WizardStepOutcome>> _renderStep;

    public WizardEngine(ITerminal terminal, IReadOnlyList<IProviderFactory> factories)
    {
        _terminal = terminal;
        _factories = factories;
        _renderStep = (step, ct) => WizardRenderer.RenderAsync(terminal, step, ct);
    }

    /// <summary>
    /// Runs the wizard. Returns a WizardResult on success, or null if the user cancelled.
    /// </summary>
    public async Task<WizardResult?> RunAsync(CancellationToken cancellationToken)
    {
        IProviderFactory? factory = null;
        WizardState state = WizardState.Empty;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (factory is null)
            {
                ChooseOneStep selectStep = BuildProviderSelectionStep();
                WizardStepOutcome selectOutcome = await _renderStep(selectStep, cancellationToken)
                    .ConfigureAwait(false);

                if (selectOutcome is WizardStepOutcome.Cancel or WizardStepOutcome.Back)
                    return null;

                string adapterName = selectStep.Options[selectStep.SelectedIndex!.Value].Value;
                factory = _factories.First(
                    f => string.Equals(f.AdapterName, adapterName, StringComparison.OrdinalIgnoreCase));
                state = new WizardState([selectStep]);
                continue;
            }

            WizardStep step = await factory.GetNextStepAsync(state, cancellationToken)
                .ConfigureAwait(false);

            if (step is WizardCompletedStep completed)
            {
                // Replaces: AnsiConsole.MarkupLine($"[green]{Markup.Escape(completed.Message)}[/]");
                _terminal.Scrollback.Append(new LineBuilder()
                    .Fg(completed.Message, Color.Named(Color.AnsiColor.Green))
                    .Build());
                return BuildResult(factory, state);
            }

            WizardStepOutcome outcome = await _renderStep(step, cancellationToken)
                .ConfigureAwait(false);

            switch (outcome)
            {
                case WizardStepOutcome.Answered:
                    List<WizardStep> newSteps = [.. state.Steps, step];
                    state = state with { Steps = newSteps };
                    break;

                case WizardStepOutcome.Back:
                    if (state.Steps.Count <= 1)
                    {
                        // Back from first factory step → return to provider selection.
                        factory = null;
                        state = WizardState.Empty;
                    }
                    else
                    {
                        List<WizardStep> trimmed = state.Steps.Take(state.Steps.Count - 1).ToList();
                        state = state with { Steps = trimmed };
                    }
                    break;

                case WizardStepOutcome.Cancel:
                    return null;
            }
        }
    }

    private ChooseOneStep BuildProviderSelectionStep() =>
        new()
        {
            Id = "adapter",
            Prompt = "Select a provider:",
            Options = _factories
                .Select(f => new WizardOption(f.DisplayName, f.AdapterName))
                .ToList(),
        };

    private static WizardResult BuildResult(IProviderFactory factory, WizardState state)
    {
        ChooseOneStep? modelStep = state.Steps
            .OfType<ChooseOneStep>()
            .FirstOrDefault(s => s.Id == "model");

        string modelId = modelStep is not null
            ? modelStep.Options[modelStep.SelectedIndex!.Value].Value
            : factory.DefaultModelId;

        return new WizardResult(factory.AdapterName, modelId, factory.DefaultEnvVar);
    }
}
