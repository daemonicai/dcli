using Dcli.Demo.DmonWizard.Steps;

namespace Dcli.Demo.DmonWizard.Providers;

/// <summary>
/// Stub Anthropic provider: exercises provider-select → model-select → API-key-input → confirm flow.
/// </summary>
internal sealed class AnthropicStub : IProviderFactory
{
    public string AdapterName => "anthropic";
    public string DisplayName => "Anthropic (Claude)";
    public string DefaultModelId => "claude-opus-4-5";
    public string DefaultEnvVar => "ANTHROPIC_API_KEY";

    public ValueTask<WizardStep> GetNextStepAsync(WizardState state, CancellationToken cancellationToken = default)
    {
        WizardStep step = state.Steps.Count switch
        {
            // Step 0 is the adapter-selection step (already answered). Steps >= 1 are factory steps.
            1 => new ChooseOneStep
            {
                Id = "model",
                Prompt = "Select a Claude model:",
                Options =
                [
                    new WizardOption("Claude Opus 4.5", "claude-opus-4-5"),
                    new WizardOption("Claude Sonnet 4.5", "claude-sonnet-4-5"),
                    new WizardOption("Claude Haiku 3.5", "claude-haiku-3-5"),
                ],
            },
            2 => new TextInputStep
            {
                Id = "api-key",
                Prompt = "Anthropic API key",
                Secret = true,
                Required = true,
                Default = null,
            },
            3 => new InfoStep
            {
                Id = "info",
                Prompt = "Anthropic configured. Validating credentials...",
            },
            _ => new WizardCompletedStep
            {
                Id = "done",
                Prompt = string.Empty,
                Message = "Anthropic provider configured successfully.",
            },
        };
        return ValueTask.FromResult(step);
    }
}
