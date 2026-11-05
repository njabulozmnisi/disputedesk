using Xunit;

namespace DisputeDesk.Domain.Tests;

public sealed class SupervisorBreakTests
{
    [Fact]
    public void FailsOnPurpose() => Assert.Equal(1, 2);
}
