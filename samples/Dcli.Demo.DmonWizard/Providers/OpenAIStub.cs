using Dcli.Demo.DmonWizard.Steps;

namespace Dcli.Demo.DmonWizard.Providers;

/// <summary>
/// Stub OpenAI provider: exercises model multi-select (demonstrates dcli's real multi-select win)
/// and a YesNo confirmation step.
/// </summary>
internal sealed class OpenAIStub : IProviderFactory
{
    public string AdapterName => "openai";
    public string DisplayName => "OpenAI (GPT)";
    public string DefaultModelId => "gpt-4o";
    public string DefaultEnvVar => "OPENAI_API_KEY";

    public ValueTask<WizardStep> GetNextStepAsync(WizardState state, CancellationToken cancellationToken = default)
    {
        WizardStep step = state.Steps.Count switch
        {
            1 => new ChooseManyStep
            {
                Id = "models",
                Prompt = "Select models to enable (Space to toggle, Enter to confirm):",
                MinSelections = 1,
                Options =
                [
                    new WizardOption("gpt-4o", "gpt-4o"),
                    new WizardOption("gpt-4o-mini", "gpt-4o-mini"),
                    new WizardOption("o1-preview", "o1-preview"),
                    new WizardOption("o1-mini", "o1-mini"),
                ],
            },
            2 => new TextInputStep
            {
                Id = "api-key",
                Prompt = "OpenAI API key",
                Secret = true,
                Required = true,
            },
            3 => new YesNoStep
            {
                Id = "confirm",
                Prompt = "Enable streaming responses?",
                Default = true,
            },
            _ => new WizardCompletedStep
            {
                Id = "done",
                Prompt = string.Empty,
                Message = "OpenAI provider configured successfully.",
            },
        };
        return ValueTask.FromResult(step);
    }
}
