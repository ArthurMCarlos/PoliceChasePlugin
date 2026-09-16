using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace PoliceChasePlugin.Tests;

internal sealed class CollectingLogSink : ILogEventSink
{
    public ConcurrentQueue<LogEvent> Events { get; } = new();

    public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);

    public bool ContainsMessage(string message) =>
        Events.Any(logEvent => logEvent.RenderMessage() == message);
}
