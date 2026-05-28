namespace Dcli.Demo.DmonWizard.Steps;

/// <summary>
/// A yes/no confirmation step.
/// Shape is identical to Dmon.Abstractions.Wizard.YesNoStep.
/// </summary>
internal sealed class YesNoStep : WizardStep
{
    public bool Default { get; init; }

    private bool? _answer;

    public bool? Answer
    {
        get => _answer;
        set
        {
            _answer = value;
            IsAnswered = value.HasValue;
        }
    }
}
