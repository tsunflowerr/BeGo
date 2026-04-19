using OptiGo.Application.Interfaces;
using OptiGo.Application.UseCases;

namespace OptiGo.Infrastructure.Routing;

public class DefaultTrafficSnapshotProvider : ITrafficSnapshotProvider
{
    public TrafficSnapshot GetCurrentSnapshot()
    {
        var utcNow = DateTimeOffset.UtcNow;
        var bucketMinute = (utcNow.Minute / 5) * 5;
        var bucket = new DateTimeOffset(
            utcNow.Year,
            utcNow.Month,
            utcNow.Day,
            utcNow.Hour,
            bucketMinute,
            0,
            TimeSpan.Zero);

        return new TrafficSnapshot(
            BucketKey: bucket.ToString("yyyyMMddHHmm"),
            CongestionMultiplier: 1.0,
            IsLive: false);
    }
}
