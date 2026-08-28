using Xunit.Abstractions;

namespace SivayaanHMS.UatTests;

/// <summary>
/// The wrong things a clinic, or a stale link, might actually do — each proving a refusal that is
/// clear about why, not just that something failed. Every scenario here is independent of the
/// others: none depends on a particular run order within the shared fixture, and none leaves
/// server-side state behind that a later test would see, because each save this suite attempts to
/// break is one the server genuinely refuses.
/// </summary>
public sealed class RegistrationUatTests(UatFixture fixture, ITestOutputHelper output) : UatTestBase(fixture, output)
{
    [Fact]
    public Task A_taken_clinic_code_is_refused_by_name() => RunWorkflowAsync("RegistrationDuplicateCode");
}

public sealed class ModuleToggleAllOffUatTests(UatFixture fixture, ITestOutputHelper output) : UatTestBase(fixture, output)
{
    [Fact]
    public Task Every_module_cannot_be_switched_off_at_once() => RunWorkflowAsync("ModuleToggleAllOff");
}

public sealed class SessionExpiryUatTests(UatFixture fixture, ITestOutputHelper output) : UatTestBase(fixture, output)
{
    [Fact]
    public Task An_invalid_session_is_bounced_to_sign_in() => RunWorkflowAsync("SessionExpiry");
}

public sealed class NotFoundUatTests(UatFixture fixture, ITestOutputHelper output) : UatTestBase(fixture, output)
{
    [Fact]
    public Task A_stale_link_gets_a_real_page_and_a_way_back() => RunWorkflowAsync("NotFoundRoute");
}

public sealed class PatientSearchNoMatchUatTests(UatFixture fixture, ITestOutputHelper output) : UatTestBase(fixture, output)
{
    [Fact]
    public Task Searching_for_nobody_says_so_and_offers_to_register_them() => RunWorkflowAsync("PatientSearchNoMatch");
}

/// <summary>Not a negative case — a functional gap the original eight left: create was covered, read was covered, update was not.</summary>
public sealed class PatientEditUatTests(UatFixture fixture, ITestOutputHelper output) : UatTestBase(fixture, output)
{
    [Fact]
    public Task Editing_a_patient_changes_the_one_record_in_place() => RunWorkflowAsync("PatientEdit");
}
