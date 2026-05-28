namespace Dcli.Demo.DmonWizard.Steps;

/// <summary>
/// An option presented in a selection step. Label is shown in the UI; Value is stored as the answer.
/// Shape is identical to Dmon.Abstractions.Wizard.WizardOption.
/// </summary>
internal sealed record WizardOption(string Label, string Value, string? Description = null);
