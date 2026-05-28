namespace Dcli.Demo.DmonWizard.Steps;

/// <summary>
/// A step asking the user to select one or more options.
/// Shape is identical to Dmon.Abstractions.Wizard.ChooseManyStep.
/// </summary>
internal sealed class ChooseManyStep : WizardStep
{
    public required IReadOnlyList<WizardOption> Options { get; init; }
    public int MinSelections { get; init; }

    private IReadOnlyList<int>? _selectedIndices;

    public IReadOnlyList<int>? SelectedIndices
    {
        get => _selectedIndices;
        set
        {
            _selectedIndices = value;
            IsAnswered = value is not null && value.Count >= MinSelections;
        }
    }
}
