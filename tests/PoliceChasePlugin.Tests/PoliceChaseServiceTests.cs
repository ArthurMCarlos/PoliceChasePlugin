using Serilog;
using PoliceChasePlugin.Ai;
using PoliceChasePlugin.Players;
using PoliceChasePlugin.Tests.Ai;
using PoliceChasePlugin.Tests.Players;

namespace PoliceChasePlugin.Tests;

[TestFixture]
[NonParallelizable]
public class PoliceChaseServiceTests
{
    private CollectingLogSink _sink = null!;
    private TestHostApplicationLifetime _lifetime = null!;

    [SetUp]
    public void SetUp()
    {
        _sink = new CollectingLogSink();
        _lifetime = new TestHostApplicationLifetime();
        Log.Logger = new LoggerConfiguration().WriteTo.Sink(_sink).CreateLogger();
    }

    [TearDown]
    public void TearDown()
    {
        Log.CloseAndFlush();
        _lifetime.Dispose();
    }

    [Test]
    public async Task EnabledServiceLogsInitializationAndShutdown()
    {
        using var service = new PoliceChaseService(
            new PoliceChaseConfiguration { Enabled = true, PoliceCarSessionId = 7 },
            CreateAiService(),
            CreateTargetService().Service,
            _lifetime);

        await service.StartAsync(CancellationToken.None);
        using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await service.StopAsync(stopTimeout.Token);

        Assert.Multiple(() =>
        {
            Assert.That(_sink.ContainsMessage("[PoliceChase] Plugin initialized"), Is.True);
            Assert.That(_sink.ContainsMessage("[PoliceChase] Plugin stopping"), Is.True);
        });
    }

    [Test]
    public async Task DisabledServiceLogsDisabledStateWithoutInitialization()
    {
        using var service = new PoliceChaseService(
            new PoliceChaseConfiguration { Enabled = false },
            CreateAiService(),
            CreateTargetService().Service,
            _lifetime);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_sink.ContainsMessage("[PoliceChase] Plugin disabled by configuration"), Is.True);
            Assert.That(_sink.ContainsMessage("[PoliceChase] Plugin initialized"), Is.False);
        });
    }

    [Test]
    public async Task EnabledServiceStartsAndStopsTargetObservation()
    {
        var (targetService, source) = CreateTargetService();
        using var service = new PoliceChaseService(
            new PoliceChaseConfiguration { Enabled = true, PoliceCarSessionId = 7 },
            CreateAiService(),
            targetService,
            _lifetime);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(source.StartCount, Is.EqualTo(1));
            Assert.That(source.StopCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task DisabledServiceDoesNotPrepareAiOrObservePlayers()
    {
        var aiSource = new FakePoliceAiSlotSource();
        var aiService = new PoliceAiService(aiSource, new PoliceChaseConfiguration());
        var (targetService, playerSource) = CreateTargetService();
        using var service = new PoliceChaseService(
            new PoliceChaseConfiguration { Enabled = false },
            aiService,
            targetService,
            _lifetime);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(aiSource.PreparedSessionId, Is.Null);
            Assert.That(playerSource.StartCount, Is.Zero);
        });
    }

    private static PoliceAiService CreateAiService()
    {
        var source = new FakePoliceAiSlotSource();
        source.AddFixedSlot(7, "police");
        source.States.Add(new FakePoliceAiState(false));
        return new PoliceAiService(source, new PoliceChaseConfiguration
        {
            PoliceCarSessionId = 7,
            PoliceCarModel = "police"
        });
    }

    private static (PoliceTargetService Service, FakePolicePlayerSource Source) CreateTargetService()
    {
        var source = new FakePolicePlayerSource();
        return (new PoliceTargetService(source), source);
    }
}
