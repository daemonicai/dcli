using Dcli.Demo.DmonWizard.Steps;

namespace Dcli.Demo.DmonWizard.Providers;

/// <summary>
/// Stub mock provider: immediately completes (no factory steps), used to verify the
/// WizardCompletedStep short-circuit in the engine.
/// </summary>
internal sealed class MockStub : IProviderFactory
{
    public string AdapterName => "mock";
    public string DisplayName => "Mock (no credentials)";
    public string DefaultModelId => "mock-v1";
    public string DefaultEnvVar => string.Empty;

    public ValueTask<WizardStep> GetNextStepAsync(WizardState state, CancellationToken cancellationToken = default)
    {
        // Immediately complete — exercises the WizardCompletedStep short-circuit path.
        WizardStep step = new WizardCompletedStep
        {
            Id = "done",
            Prompt = string.Empty,
            Message = "Mock provider ready — no credentials required.",
        };
        return ValueTask.FromResult(step);
    }
}
