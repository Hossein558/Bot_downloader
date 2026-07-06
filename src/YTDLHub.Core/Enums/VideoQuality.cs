namespace YTDLHub.Core.Enums;

public enum VideoQuality
{
    Best,
    HD1080,
    HD720,
    SD480,
    SD360,
    AudioMp3,
    AudioM4A
}

public static class VideoQualityExtensions
{
    /// <summary>
    /// Returns the yt-dlp format selector string for the given quality preset.
    /// </summary>
    public static string ToFormatSelector(this VideoQuality quality) => quality switch
    {
        VideoQuality.Best     => "bestvideo+bestaudio/best",
        VideoQuality.HD1080   => "bestvideo[height<=1080]+bestaudio/best[height<=1080]",
        VideoQuality.HD720    => "bestvideo[height<=720]+bestaudio/best[height<=720]",
        VideoQuality.SD480    => "bestvideo[height<=480]+bestaudio/best[height<=480]",
        VideoQuality.SD360    => "bestvideo[height<=360]+bestaudio/best[height<=360]",
        VideoQuality.AudioMp3 => "bestaudio",
        VideoQuality.AudioM4A => "bestaudio",
        _                     => "bestvideo+bestaudio/best"
    };

    public static string ToDisplayName(this VideoQuality quality) => quality switch
    {
        VideoQuality.Best     => "🏆 بهترین کیفیت",
        VideoQuality.HD1080   => "📺 1080p (Full HD)",
        VideoQuality.HD720    => "🖥️ 720p (HD)",
        VideoQuality.SD480    => "📱 480p (SD)",
        VideoQuality.SD360    => "🐌 360p (کم حجم)",
        VideoQuality.AudioMp3 => "🎵 فقط صدا (MP3)",
        VideoQuality.AudioM4A => "🎶 فقط صدا (M4A)",
        _                     => quality.ToString()
    };

    public static string ToFileExtension(this VideoQuality quality) => quality switch
    {
        VideoQuality.AudioMp3 => "mp3",
        VideoQuality.AudioM4A => "m4a",
        _                     => "mp4"
    };
}
