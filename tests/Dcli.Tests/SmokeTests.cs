using Xunit;

namespace Dcli.Tests;

/// <summary>
/// Placeholder tests confirming the assembly loads. Behavioral tests are added per section.
/// </summary>
public sealed class SmokeTests
{
    [Fact]
    public void AssemblyLoads()
    {
        // Verifies that the Dcli assembly can be referenced and loaded by the test runner.
        System.Reflection.Assembly assembly = typeof(DcliVersion).Assembly;
        Assert.Equal("Dcli", assembly.GetName().Name);
    }
}
