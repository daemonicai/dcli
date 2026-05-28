namespace Dcli.Demo.DmonWizard.Steps;

/// <summary>
/// A step asking the user to pick exactly one option from a list.
/// Shape is identical to Dmon.Abstractions.Wizard.ChooseOneStep.
/// </summary>
internal sealed class ChooseOneStep : WizardStep
{
    public required IReadOnlyList<WizardOption> Options { get; init; }

    private int? _selectedIndex;

    public int? SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            _selectedIndex = value;
            IsAnswered = value.HasValue;
        }
    }
}
