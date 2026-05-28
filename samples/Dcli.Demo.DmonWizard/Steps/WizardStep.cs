namespace Dcli.Demo.DmonWizard.Steps;

/// <summary>
/// Abstract base for all wizard steps. Shape is identical to Dmon.Abstractions.Wizard.WizardStep.
/// </summary>
internal abstract class WizardStep
{
    public required string Id { get; init; }
    public required string Prompt { get; init; }
    public bool IsAnswered { get; protected set; }
}
