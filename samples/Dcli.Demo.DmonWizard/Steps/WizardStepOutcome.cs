namespace Dcli.Demo.DmonWizard.Steps;

/// <summary>
/// The outcome of rendering a single wizard step.
/// Shape is identical to Dmon.Terminal.WizardStepOutcome.
/// </summary>
internal enum WizardStepOutcome
{
    Answered,
    Back,
    Cancel,
}
