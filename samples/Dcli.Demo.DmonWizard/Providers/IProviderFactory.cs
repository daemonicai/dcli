using Dcli.Demo.DmonWizard.Steps;

namespace Dcli.Demo.DmonWizard.Providers;

/// <summary>
/// Minimal provider-factory contract — shape matches Dmon.Abstractions.Providers.IProviderFactory
/// minus the Microsoft.Extensions.AI types (not available in this standalone port).
/// </summary>
internal interface IProviderFactory
{
    string AdapterName { get; }
    string DisplayName { get; }
    string DefaultModelId { get; }
    string DefaultEnvVar { get; }

    /// <summary>
    /// Returns the next wizard step to present given the steps answered so far, or a
    /// WizardCompletedStep when setup is complete.
    /// </summary>
    ValueTask<WizardStep> GetNextStepAsync(WizardState state, CancellationToken cancellationToken = default);
}
