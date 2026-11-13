using System.Threading.Channels;

namespace Msem001.Watchdog.Alerting;

/// <summary>
/// Decouples logging (synchronous, any thread) from sending mails (asynchronous, background).
/// When full the oldest entries are dropped, logging never blocks.
/// </summary>
internal sealed class AlertQueue
{
    internal const int Capacity = 500;

    private readonly Channel<AlertEntry> channel = Channel.CreateBounded<AlertEntry>(new BoundedChannelOptions(Capacity)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false,
    });

    public ChannelReader<AlertEntry> Reader => channel.Reader;

    public bool TryEnqueue(AlertEntry entry) => channel.Writer.TryWrite(entry);
}
