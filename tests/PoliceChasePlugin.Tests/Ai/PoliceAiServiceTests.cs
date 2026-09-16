using AssettoServer.Server;
using PoliceChasePlugin.Ai;

namespace PoliceChasePlugin.Tests.Ai;

[TestFixture]
public class PoliceAiServiceTests
{
    [Test]
    public void PreparesOnlyConfiguredFixedSlot()
    {
        var source = new FakePoliceAiSlotSource();
        source.AddFixedSlot(2, "traffic");
        source.AddFixedSlot(7, "police");
        source.States.Add(new FakePoliceAiState(false));
        var service = Create(source, 7, "police");

        service.Prepare();

        Assert.Multiple(() =>
        {
            Assert.That(source.PreparedSessionId, Is.EqualTo(7));
            Assert.That(service.SelectedSlot?.SessionId, Is.EqualTo(7));
            Assert.That(service.SelectedState?.IsInitialized, Is.False);
        });
    }

    [Test]
    public void RejectsMissingSlotWithoutFallback()
    {
        var source = new FakePoliceAiSlotSource();
        source.AddFixedSlot(2, "traffic");
        var service = Create(source, 7, "police");

        var exception = Assert.Throws<InvalidOperationException>(() => service.Prepare());

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("slot 7 was not found"));
            Assert.That(source.PreparedSessionId, Is.Null);
        });
    }

    [Test]
    public void RejectsSlotThatIsNotFixed()
    {
        var source = new FakePoliceAiSlotSource();
        source.Slots.Add(new PoliceAiSlotInfo(7, "police", AiMode.Auto));
        var service = Create(source, 7, "police");

        var exception = Assert.Throws<InvalidOperationException>(() => service.Prepare());

        Assert.That(exception!.Message, Does.Contain("not configured as AI=FIXED"));
    }

    [Test]
    public void RejectsConfiguredModelMismatch()
    {
        var source = new FakePoliceAiSlotSource();
        source.AddFixedSlot(7, "actual");
        var service = Create(source, 7, "expected");

        var exception = Assert.Throws<InvalidOperationException>(() => service.Prepare());

        Assert.That(exception!.Message, Does.Contain("model mismatch"));
    }

    [Test]
    public void EmptyConfiguredModelDoesNotRejectSlot()
    {
        var source = new FakePoliceAiSlotSource();
        source.AddFixedSlot(7, "police");
        source.States.Add(new FakePoliceAiState(true));
        var service = Create(source, 7, "");

        service.Prepare();

        Assert.That(service.SelectedState?.IsInitialized, Is.True);
    }

    [TestCase(0)]
    [TestCase(2)]
    public void RejectsStateCountOtherThanExactlyOne(int stateCount)
    {
        var source = new FakePoliceAiSlotSource();
        source.AddFixedSlot(7, "police");
        for (var index = 0; index < stateCount; index++)
            source.States.Add(new FakePoliceAiState(false));
        var service = Create(source, 7, "police");

        var exception = Assert.Throws<InvalidOperationException>(() => service.Prepare());

        Assert.That(exception!.Message, Does.Contain("exactly one state"));
    }

    private static PoliceAiService Create(
        FakePoliceAiSlotSource source,
        int sessionId,
        string model) => new(source, new PoliceChaseConfiguration
    {
        PoliceCarSessionId = sessionId,
        PoliceCarModel = model
    });
}
