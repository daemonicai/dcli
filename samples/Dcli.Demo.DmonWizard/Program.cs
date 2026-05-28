// dcli API ergonomics validator — dmon WizardEngine/WizardRenderer port.
// Exercises the multi-step wizard flow (provider-select → model-select → API-key → confirm)
// against dcli's SelectAsync / MultiSelectAsync / InputAsync / ChoiceAsync / Scrollback.Append.
//
// To run interactively, replace `cts.Token` with `CancellationToken.None` in the engine.RunAsync call.
// Run: dotnet run --project samples/Dcli.Demo.DmonWizard

using Dcli;
using Dcli.Demo.DmonWizard.Engine;
using Dcli.Demo.DmonWizard.Providers;

await using Terminal t = await Terminal.StartAsync(new TerminalOptions
{
    MaxFixedHeight = 14,
    MinFrameIntervalMs = 16,
});

t.Status.SetRows(new LineBuilder()
    .Bold("dcli wizard port")
    .Dim(" -- dmon WizardEngine ergonomics validator")
    .Build());

t.Scrollback.Append(new LineBuilder()
    .Bold("dmon WizardEngine port")
    .Dim(" -- dcli API ergonomics validator")
    .Build());

t.Scrollback.Append(new LineBuilder()
    .Dim("Exercises: SelectAsync / MultiSelectAsync / InputAsync / ChoiceAsync / Scrollback.Append")
    .Build());

t.Scrollback.Append(new LineBuilder().Text(string.Empty).Build());

// Stub provider factories — representative of the real dmon provider graph.
IReadOnlyList<IProviderFactory> factories =
[
    new AnthropicStub(),
    new OpenAIStub(),
    new MockStub(),
];

WizardEngine engine = new(t, factories);

// Auto-cancel after 10s so the binary self-exits without input in CI / demo mode.
// To run interactively, replace `cts.Token` with `CancellationToken.None`.
using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));

WizardResult? result;
try
{
    result = await engine.RunAsync(cts.Token).ConfigureAwait(false);
}
catch (OperationCanceledException)
{
    result = null;
}

t.Scrollback.Append(new LineBuilder().Text(string.Empty).Build());

if (result is not null)
{
    t.Scrollback.Append(new LineBuilder()
        .Fg("Wizard completed.", Color.Named(Color.AnsiColor.BrightGreen))
        .Build());
    t.Scrollback.Append(new LineBuilder()
        .Text("  Adapter: ")
        .Bold(result.Adapter)
        .Build());
    t.Scrollback.Append(new LineBuilder()
        .Text("  Model: ")
        .Bold(result.ModelId)
        .Build());
    t.Scrollback.Append(new LineBuilder()
        .Text("  Env var: ")
        .Bold(result.EnvVar)
        .Build());
}
else
{
    t.Scrollback.Append(new LineBuilder()
        .Fg("Wizard cancelled.", Color.Named(Color.AnsiColor.Yellow))
        .Build());
}

t.Status.SetRows(new LineBuilder()
    .Fg("Done — exiting in 2s.", Color.Named(Color.AnsiColor.BrightGreen))
    .Build());

await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
