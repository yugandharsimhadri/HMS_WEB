using SivayaanHMS.Automation;

namespace SivayaanHMS.UatTests;

/// <summary>
/// Shared setup for the whole UAT run: brings the API and the client's dev server up once, and
/// seeds the dataset once, rather than doing either per test. Every test then opens its own
/// browser session against them, so no scenario inherits a browser another one left mid-journey —
/// the same shape TransTrack.Automation's UatFixture uses for the same reason.
///
/// Chromium is provisioned lazily by <see cref="BrowserProvisioning"/> on first launch, not here.
/// </summary>
public sealed class UatFixture : IAsyncLifetime
{
    private ApiServer? _api;
    private WebDevServer? _web;

    public AutomationOptions Options { get; private set; } = AutomationOptions.FromEnvironment();

    public Task InitializeAsync()
    {
        Options = AutomationOptions.FromEnvironment();
        return InitializeCoreAsync();
    }

    private async Task InitializeCoreAsync()
    {
        // API first: the dev server is started with VITE_API_URL pointing at it, and the seed has
        // to be in place before any test signs in.
        _api = await ApiServer.StartAsync(Options, Console.WriteLine);
        await new DemoDataSeeder(Options.ApiBaseUrl, Console.WriteLine).SeedAsync();
        _web = await WebDevServer.StartAsync(Options, Console.WriteLine);
    }

    public async Task DisposeAsync()
    {
        if (_web is not null) await _web.DisposeAsync();
        if (_api is not null) await _api.DisposeAsync();
    }
}

/// <summary>
/// Puts every UAT class in one collection so they share the fixture and run one after another.
/// They drive real browsers against one dev server and one throwaway database; running them in
/// parallel would contend for both, and two scenarios writing patients or settings at once would
/// make each other's assertions unpredictable — the module-toggle scenario in particular depends
/// on running before nothing else has touched the same setting.
/// </summary>
[CollectionDefinition(Name)]
public sealed class UatCollection : ICollectionFixture<UatFixture>
{
    public const string Name = "SivayaanHMS UAT";
}
