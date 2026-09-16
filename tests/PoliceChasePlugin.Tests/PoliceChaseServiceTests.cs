using Serilog;

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
            new PoliceChaseConfiguration { Enabled = true },
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
            _lifetime);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_sink.ContainsMessage("[PoliceChase] Plugin disabled by configuration"), Is.True);
            Assert.That(_sink.ContainsMessage("[PoliceChase] Plugin initialized"), Is.False);
        });
    }
}
