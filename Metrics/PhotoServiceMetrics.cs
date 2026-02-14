using System.Diagnostics.Metrics;

namespace PhotoService.Metrics;

public sealed class PhotoServiceMetrics
{
    public const string MeterName = "PhotoService";

    private readonly Counter<long> _photosUploaded;
    private readonly Counter<long> _photosDeleted;
    private readonly Histogram<double> _processingDuration;
    private readonly Histogram<double> _moderationScore;

    public PhotoServiceMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _photosUploaded = meter.CreateCounter<long>("photos_uploaded_total",
            description: "Total number of photos uploaded");
        _photosDeleted = meter.CreateCounter<long>("photos_deleted_total",
            description: "Total number of photos deleted");
        _processingDuration = meter.CreateHistogram<double>("photo_processing_duration_ms",
            unit: "ms",
            description: "Duration of photo processing in milliseconds");
        _moderationScore = meter.CreateHistogram<double>("photo_moderation_score",
            description: "Distribution of photo moderation safety scores");
    }

    public void PhotoUploaded() => _photosUploaded.Add(1);
    public void PhotoDeleted() => _photosDeleted.Add(1);
    public void RecordProcessingDuration(double ms) => _processingDuration.Record(ms);
    public void RecordModerationScore(double score) => _moderationScore.Record(score);
}
