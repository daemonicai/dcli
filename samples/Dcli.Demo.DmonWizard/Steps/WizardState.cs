namespace Dcli.Demo.DmonWizard.Steps;

/// <summary>
/// Accumulates the ordered list of answered WizardStep objects.
/// Index 0 is the adapter-selection step; subsequent entries are factory-produced.
/// Shape is identical to Dmon.Abstractions.Wizard.WizardState.
/// </summary>
internal sealed record WizardState(IReadOnlyList<WizardStep> Steps)
{
    public static readonly WizardState Empty = new([]);
}
