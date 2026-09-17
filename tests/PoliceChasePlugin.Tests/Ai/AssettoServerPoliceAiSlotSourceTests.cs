using AssettoServer.Server;
using PoliceChasePlugin.Ai;

namespace PoliceChasePlugin.Tests.Ai;

[TestFixture]
public class AssettoServerPoliceAiSlotSourceTests
{
    [Test]
    public void ReservesExactlyOneStateOnlyOnSelectedNativeSlot()
    {
        var traffic = new FakeNativePoliceAiSlot(2, "traffic", AiMode.Fixed)
        {
            AiMaxOverbooking = 3
        };
        var police = new FakeNativePoliceAiSlot(7, "police", AiMode.Fixed);
        police.States.Add(new FakePoliceAiState(false));
        var source = new AssettoServerPoliceAiSlotSource(
            new INativePoliceAiSlot[] { traffic, police });

        var states = source.PrepareSingleState(7);

        Assert.Multiple(() =>
        {
            Assert.That(police.AiMinOverbooking, Is.EqualTo(1));
            Assert.That(police.AiMaxOverbooking, Is.EqualTo(1));
            Assert.That(police.AiControlRequests, Is.EqualTo(new[] { true }));
            Assert.That(police.OverbookingRequests, Is.EqualTo(new[] { 1 }));
            Assert.That(states, Is.EqualTo(police.States));

            Assert.That(traffic.AiMinOverbooking, Is.Zero);
            Assert.That(traffic.AiMaxOverbooking, Is.EqualTo(3));
            Assert.That(traffic.AiControlRequests, Is.Empty);
            Assert.That(traffic.OverbookingRequests, Is.Empty);
        });
    }

    private sealed class FakeNativePoliceAiSlot : INativePoliceAiSlot
    {
        public byte SessionId { get; }
        public string Model { get; }
        public AiMode Mode { get; }
        public int AiMinOverbooking { get; set; }
        public int? AiMaxOverbooking { get; set; }
        public List<bool> AiControlRequests { get; } = new();
        public List<int> OverbookingRequests { get; } = new();
        public List<IPoliceAiState> States { get; } = new();

        public FakeNativePoliceAiSlot(byte sessionId, string model, AiMode mode)
        {
            SessionId = sessionId;
            Model = model;
            Mode = mode;
        }

        public void SetAiControl(bool aiControlled) =>
            AiControlRequests.Add(aiControlled);

        public void SetAiOverbooking(int count) =>
            OverbookingRequests.Add(count);

        public IReadOnlyList<IPoliceAiState> GetStates() => States;
    }
}
