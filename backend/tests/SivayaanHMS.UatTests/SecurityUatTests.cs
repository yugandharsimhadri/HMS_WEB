using Xunit.Abstractions;

namespace SivayaanHMS.UatTests;

/// <summary>
/// The door every screen sits behind: a wrong password refused, the right one let through, and
/// the session surviving a reload.
/// </summary>
public sealed class SignInUatTests(UatFixture fixture, ITestOutputHelper output) : UatTestBase(fixture, output)
{
    [Fact]
    public Task Signing_in_with_a_real_password_and_being_refused_a_wrong_one() => RunWorkflowAsync("SignIn");
}
