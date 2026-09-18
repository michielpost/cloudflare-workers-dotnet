namespace Shared;

// R2 sample: the bucket, the fixed object key every upload overwrites and the
// upload size limit.
public static partial class SampleConfig
{
    /// <summary>R2 bucket name from wrangler.toml.</summary>
    public const string R2BucketName = "dotnettest";

    /// <summary>Every upload is stored under this key, so uploads overwrite each other.</summary>
    public const string R2ObjectKey = "sample_file.txt";

    /// <summary>R2 uploads are capped at this many bytes.</summary>
    public const int R2MaxBytes = 1024;
}
