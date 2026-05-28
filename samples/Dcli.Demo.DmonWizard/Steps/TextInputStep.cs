namespace Dcli.Demo.DmonWizard.Steps;

/// <summary>
/// A step asking for free-form text. Secret input is masked in the terminal.
/// Shape is identical to Dmon.Abstractions.Wizard.TextInputStep.
/// </summary>
internal sealed class TextInputStep : WizardStep
{
    public string? Default { get; init; }
    public bool Secret { get; init; }
    public bool Required { get; init; }

    private string? _value;

    public string? Value
    {
        get => _value;
        set
        {
            _value = value;
            IsAnswered = value is not null;
        }
    }
}
