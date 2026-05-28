namespace Dcli.Demo.DmonWizard.Steps;

/// <summary>
/// Terminal marker returned by a factory when setup is complete.
/// Shape is identical to Dmon.Abstractions.Wizard.WizardCompletedStep.
/// </summary>
internal sealed class WizardCompletedStep : WizardStep
{
    public required string Message { get; init; }
}
