using System.Text.RegularExpressions;
using MediaFileDLConverter.Models;

namespace MediaFileDLConverter.Services
{
    /// <summary>
    /// Parses media URLs to detect platform type and extract playlist information.
    /// </summary>
    public static class LinkParserService
    {
        // YouTube patterns
        private static readonly Regex YoutubeVideoRegex = new(
            @"(?:https?://)?(?:www\.)?(?:youtube\.com/watch\?v=|youtu\.be/)[\w\-]+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex YoutubePlaylistRegex = new(
            @"[?&]list=([\w\-]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex YoutubeRegex = new(
            @"(?:https?://)?(?:www\.)?(?:youtube\.com|youtu\.be)/",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // SoundCloud pattern
        private static readonly Regex SoundCloudRegex = new(
            @"(?:https?://)?(?:www\.)?soundcloud\.com/",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // MixCloud pattern
        private static readonly Regex MixCloudRegex = new(
            @"(?:https?://)?(?:www\.)?mixcloud\.com/",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Determines the platform type from a given URL.
        /// </summary>
        public static LinkType DetectLinkType(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return LinkType.Unknown;

            url = url.Trim();

            if (YoutubeRegex.IsMatch(url))
                return LinkType.YouTube;
            if (SoundCloudRegex.IsMatch(url))
                return LinkType.SoundCloud;
            if (MixCloudRegex.IsMatch(url))
                return LinkType.MixCloud;

            return LinkType.Unknown;
        }

        /// <summary>
        /// Checks if a YouTube URL contains a playlist parameter.
        /// </summary>
        public static bool HasPlaylist(string url)
        {
            return YoutubePlaylistRegex.IsMatch(url);
        }

        /// <summary>
        /// Extracts the playlist ID from a YouTube URL.
        /// </summary>
        public static string? ExtractPlaylistId(string url)
        {
            var match = YoutubePlaylistRegex.Match(url);
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// Checks if a playlist ID is a YouTube radio/mix (auto-generated).
        /// These start with "RD" and cannot be fetched via /playlist?list= URL.
        /// </summary>
        public static bool IsRadioMix(string playlistId)
        {
            return playlistId.StartsWith("RD", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Builds a playlist URL suitable for yt-dlp to enumerate.
        /// For standard playlists: uses /playlist?list=ID
        /// For radio/mix: uses the original watch URL with list= param (yt-dlp handles it)
        /// </summary>
        public static string BuildPlaylistUrl(string playlistId, string? originalUrl = null)
        {
            // Radio/mix playlists need the original URL with list= param
            if (IsRadioMix(playlistId) && !string.IsNullOrEmpty(originalUrl))
                return originalUrl;

            return $"https://www.youtube.com/playlist?list={playlistId}";
        }

        /// <summary>
        /// Gets the allowed output formats for a given link type.
        /// </summary>
        public static OutputFormat[] GetAllowedFormats(LinkType linkType)
        {
            return linkType switch
            {
                LinkType.YouTube => [OutputFormat.MP3, OutputFormat.WAV, OutputFormat.MP4Low, OutputFormat.MP4High],
                LinkType.SoundCloud => [OutputFormat.MP3, OutputFormat.WAV],
                LinkType.MixCloud => [OutputFormat.MP3, OutputFormat.WAV],
                _ => []
            };
        }

        /// <summary>
        /// Validates that a URL is a valid, parseable media link.
        /// </summary>
        public static bool IsValidUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            return DetectLinkType(url) != LinkType.Unknown;
        }
    }
}
