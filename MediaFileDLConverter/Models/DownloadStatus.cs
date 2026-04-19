namespace MediaFileDLConverter.Models
{
    /// <summary>
    /// Represents the current status of a download item.
    /// </summary>
    public enum DownloadStatus
    {
        Queued,
        Downloading,
        Converting,
        Completed,
        Failed,
        Cancelled
    }
}
