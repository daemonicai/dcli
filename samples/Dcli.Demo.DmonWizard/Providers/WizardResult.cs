namespace Dcli.Demo.DmonWizard.Providers;

/// <summary>
/// The result produced by a completed wizard run.
/// Shape matches Dmon.Terminal.WizardResult.
/// </summary>
internal sealed record WizardResult(string Adapter, string ModelId, string EnvVar);
