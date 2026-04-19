using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using MediaFileDLConverter.Models;

namespace MediaFileDLConverter.Services
{
    /// <summary>
    /// Orchestrates downloads using yt-dlp and ffmpeg.
    /// Manages process execution, progress parsing, and cancellation.
    /// </summary>
    public class DownloadService
    {
        private readonly SettingsService _settings;

        // Path to yt-dlp executable — look in Tools subfolder first, then PATH
        private string? _ytDlpPath;
        private string? _ffmpegPath;

        public DownloadService(SettingsService settings)
        {
            _settings = settings;
            ResolveToolPaths();
        }

        /// <summary>
        /// Resolves paths to yt-dlp and ffmpeg executables.
        /// </summary>
        private void ResolveToolPaths()
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var toolsDir = Path.Combine(appDir, "Tools");

            // Check Tools folder first
            var ytDlpInTools = Path.Combine(toolsDir, "yt-dlp.exe");
            var ffmpegInTools = Path.Combine(toolsDir, "ffmpeg.exe");

            if (File.Exists(ytDlpInTools))
                _ytDlpPath = ytDlpInTools;
            else
                _ytDlpPath = FindInPath("yt-dlp.exe") ?? FindInPath("yt-dlp");

            if (File.Exists(ffmpegInTools))
                _ffmpegPath = ffmpegInTools;
            else
                _ffmpegPath = FindInPath("ffmpeg.exe") ?? FindInPath("ffmpeg");
        }

        /// <summary>
        /// Checks whether yt-dlp is available.
        /// </summary>
        public bool IsYtDlpAvailable => _ytDlpPath != null;

        /// <summary>
        /// Checks whether ffmpeg is available.
        /// </summary>
        public bool IsFfmpegAvailable => _ffmpegPath != null;

        /// <summary>
        /// Returns a human-readable status of tool availability.
        /// </summary>
        public string GetToolStatus()
        {
            var issues = new List<string>();
            if (!IsYtDlpAvailable) issues.Add("yt-dlp not found");
            if (!IsFfmpegAvailable) issues.Add("ffmpeg not found");

            if (issues.Count == 0)
                return "All tools ready";

            return string.Join(", ", issues) + " — place executables in the Tools folder or add to PATH";
        }

        /// <summary>
        /// Downloads a single media item with the specified format.
        /// For MP3: downloads as WAV first, then converts to MP3 via ffmpeg.
        /// Reports progress via the callback. Can be cancelled via the token.
        /// </summary>
        public async Task<DownloadResult> DownloadAsync(
            string url,
            OutputFormat format,
            DownloadMode mode,
            Action<double, string> progressCallback,
            CancellationToken cancellationToken)
        {
            if (!IsYtDlpAvailable)
                return new DownloadResult(false, "yt-dlp not found. Place yt-dlp.exe in the Tools folder.");

            if (!IsFfmpegAvailable)
                return new DownloadResult(false, "ffmpeg not found. Place ffmpeg.exe in the Tools folder.");

            var outputDir = _settings.CurrentSettings.DownloadDirectory;
            if (string.IsNullOrWhiteSpace(outputDir))
                outputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

            Directory.CreateDirectory(outputDir);

            try
            {
                if (format == OutputFormat.MP3)
                {
                    // MP3 two-step: download as WAV, then convert to MP3
                    return await DownloadAsMp3Async(url, mode, outputDir, progressCallback, cancellationToken);
                }
                else
                {
                    var args = BuildArguments(url, format, mode, outputDir);
                    return await RunYtDlpAsync(args, progressCallback, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                return new DownloadResult(false, "Download cancelled");
            }
            catch (Exception ex)
            {
                return new DownloadResult(false, $"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// MP3 two-step process: download as WAV via yt-dlp, then convert WAV→MP3 via ffmpeg.
        /// This avoids yt-dlp's problematic ffprobe codec check during MP3 extraction.
        /// </summary>
        private async Task<DownloadResult> DownloadAsMp3Async(
            string url,
            DownloadMode mode,
            string outputDir,
            Action<double, string> progressCallback,
            CancellationToken cancellationToken)
        {
            // Step 1: Download as WAV, capturing the output file path from yt-dlp
            progressCallback(-1, "Step 1/2: Downloading audio as WAV...");

            var capturedFiles = new List<string>();
            var wavArgs = BuildArguments(url, OutputFormat.WAV, mode, outputDir);
            var wavResult = await RunYtDlpAsync(wavArgs, (progress, msg) =>
            {
                // Capture WAV output filenames from yt-dlp output
                // yt-dlp prints: [ExtractAudio] Destination: /path/to/file.wav
                if (msg.Contains("[ExtractAudio] Destination:"))
                {
                    var path = msg.Replace("[ExtractAudio] Destination:", "").Trim();
                    if (!string.IsNullOrEmpty(path))
                        capturedFiles.Add(path);
                }

                // Scale progress to 0-70% for the download step
                if (progress >= 0)
                    progressCallback(progress * 0.7, $"Downloading: {progress:F1}%");
                else
                    progressCallback(-1, msg);
            }, cancellationToken);

            if (!wavResult.Success)
                return wavResult;

            // If we didn't capture any files from stdout, try finding them in the output dir
            if (capturedFiles.Count == 0)
            {
                capturedFiles = Directory.GetFiles(outputDir, "*.wav")
                    .Where(f => File.GetLastWriteTime(f) > DateTime.Now.AddMinutes(-5))
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .ToList();
            }

            if (capturedFiles.Count == 0)
            {
                return new DownloadResult(false, "WAV download succeeded but file not found for MP3 conversion");
            }

            // Step 2: Convert each WAV file to MP3
            progressCallback(70, "Step 2/2: Converting WAV to MP3 (320kbps, 44100Hz)...");

            var convertedCount = 0;
            foreach (var wavFile in capturedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!File.Exists(wavFile))
                    continue;

                var mp3File = Path.ChangeExtension(wavFile, ".mp3");

                // Convert WAV to MP3: 320kbps CBR, 44100Hz
                var ffmpegArgs = $"-y -i \"{wavFile}\" -codec:a libmp3lame -b:a 320k -ar 44100 \"{mp3File}\"";

                progressCallback(-1, $"Converting: {Path.GetFileNameWithoutExtension(wavFile)}");

                var convertResult = await RunFfmpegAsync(ffmpegArgs, (progress, msg) =>
                {
                    progressCallback(85, "Converting to MP3...");
                }, cancellationToken);

                if (convertResult.Success && File.Exists(mp3File))
                {
                    // Delete the intermediate WAV file
                    try { File.Delete(wavFile); } catch { }
                    convertedCount++;
                }
                else
                {
                    return new DownloadResult(false, $"MP3 conversion failed: {convertResult.Message}");
                }
            }

            progressCallback(100, "Complete");
            return new DownloadResult(true, $"Downloaded and converted {convertedCount} file(s) to MP3");
        }

        /// <summary>
        /// Gets the list of items in a playlist without downloading them.
        /// Returns a list of (title, url) tuples.
        /// </summary>
        public async Task<List<(string Title, string Url)>> GetPlaylistItemsAsync(
            string url,
            CancellationToken cancellationToken)
        {
            if (!IsYtDlpAvailable)
                return new List<(string, string)>();

            var args = $"--flat-playlist --print \"%(title)s|||%(webpage_url)s\" --no-warnings \"{url}\"";
            var items = new List<(string Title, string Url)>();

            var psi = new ProcessStartInfo
            {
                FileName = _ytDlpPath!,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            while (!process.StandardOutput.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await process.StandardOutput.ReadLineAsync();
                if (line != null && line.Contains("|||"))
                {
                    var parts = line.Split("|||", 2);
                    if (parts.Length == 2)
                    {
                        items.Add((parts[0].Trim(), parts[1].Trim()));
                    }
                }
            }

            await process.WaitForExitAsync(cancellationToken);
            return items;
        }

        /// <summary>
        /// Builds yt-dlp command line arguments for the given configuration.
        /// Note: MP3 is handled separately via DownloadAsMp3Async (WAV download + ffmpeg convert).
        /// </summary>
        private string BuildArguments(string url, OutputFormat format, DownloadMode mode, string outputDir)
        {
            var playlistFlag = mode == DownloadMode.Playlist ? "--yes-playlist" : "--no-playlist";
            var outputTemplate = Path.Combine(outputDir, "%(title)s.%(ext)s");

            // Add ffmpeg location if we know it
            var ffmpegLocation = "";
            if (_ffmpegPath != null)
            {
                var ffmpegDir = Path.GetDirectoryName(_ffmpegPath);
                if (!string.IsNullOrEmpty(ffmpegDir))
                    ffmpegLocation = $"--ffmpeg-location \"{ffmpegDir}\"";
            }

            var args = format switch
            {
                // WAV: 16-bit, 44100Hz (also used as intermediate for MP3)
                OutputFormat.WAV =>
                    $"{playlistFlag} {ffmpegLocation} -x --audio-format wav " +
                    $"--postprocessor-args \"ffmpeg:-ar 44100 -acodec pcm_s16le\" " +
                    $"--no-warnings -o \"{outputTemplate}\" \"{url}\"",

                // MP4 Low Quality: 720p, x264
                OutputFormat.MP4Low =>
                    $"{playlistFlag} {ffmpegLocation} " +
                    $"-f \"bestvideo[height<=720][vcodec^=avc1]+bestaudio/best[height<=720]\" " +
                    $"--merge-output-format mp4 " +
                    $"--postprocessor-args \"ffmpeg:-c:v libx264 -c:a aac\" " +
                    $"--no-warnings -o \"{outputTemplate}\" \"{url}\"",

                // MP4 High Quality: up to 4K, x264
                OutputFormat.MP4High =>
                    $"{playlistFlag} {ffmpegLocation} " +
                    $"-f \"bestvideo[vcodec^=avc1]+bestaudio/bestvideo+bestaudio/best\" " +
                    $"--merge-output-format mp4 " +
                    $"--postprocessor-args \"ffmpeg:-c:v libx264 -c:a aac\" " +
                    $"--no-warnings -o \"{outputTemplate}\" \"{url}\"",

                // MP3 should not reach here (handled by DownloadAsMp3Async)
                OutputFormat.MP3 => throw new InvalidOperationException("MP3 is handled by DownloadAsMp3Async"),

                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };

            return args;
        }

        /// <summary>
        /// Runs yt-dlp as an external process and parses progress from stdout/stderr.
        /// </summary>
        private async Task<DownloadResult> RunYtDlpAsync(
            string arguments,
            Action<double, string> progressCallback,
            CancellationToken cancellationToken)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _ytDlpPath!,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            var errorOutput = new List<string>();

            process.Start();

            // Register cancellation to kill the process
            using var registration = cancellationToken.Register(() =>
            {
                try { process.Kill(true); } catch { }
            });

            // Read stdout for progress
            _ = Task.Run(async () =>
            {
                while (!process.StandardOutput.EndOfStream)
                {
                    var line = await process.StandardOutput.ReadLineAsync();
                    if (line != null)
                    {
                        var progress = ParseYtDlpProgress(line);
                        if (progress.HasValue)
                        {
                            progressCallback(progress.Value, line.Trim());
                        }
                        else
                        {
                            progressCallback(-1, line.Trim());
                        }
                    }
                }
            }, cancellationToken);

            // Read stderr for errors
            _ = Task.Run(async () =>
            {
                while (!process.StandardError.EndOfStream)
                {
                    var line = await process.StandardError.ReadLineAsync();
                    if (line != null)
                    {
                        errorOutput.Add(line);
                        progressCallback(-1, line.Trim());
                    }
                }
            }, cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            if (cancellationToken.IsCancellationRequested)
                return new DownloadResult(false, "Download cancelled");

            if (process.ExitCode != 0)
            {
                var errorMsg = errorOutput.Count > 0
                    ? string.Join(Environment.NewLine, errorOutput.TakeLast(3))
                    : "Unknown error occurred";
                return new DownloadResult(false, errorMsg);
            }

            return new DownloadResult(true, "Download completed successfully");
        }

        /// <summary>
        /// Runs ffmpeg as an external process for format conversion.
        /// </summary>
        private async Task<DownloadResult> RunFfmpegAsync(
            string arguments,
            Action<double, string> progressCallback,
            CancellationToken cancellationToken)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath!,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            var errorOutput = new List<string>();

            process.Start();

            using var registration = cancellationToken.Register(() =>
            {
                try { process.Kill(true); } catch { }
            });

            // ffmpeg outputs progress to stderr
            _ = Task.Run(async () =>
            {
                while (!process.StandardError.EndOfStream)
                {
                    var line = await process.StandardError.ReadLineAsync();
                    if (line != null)
                    {
                        errorOutput.Add(line);
                        // ffmpeg progress lines contain "time=" and "size="
                        if (line.Contains("time=") || line.Contains("size="))
                        {
                            progressCallback(-1, "Converting to MP3...");
                        }
                    }
                }
            }, cancellationToken);

            // Also drain stdout
            _ = Task.Run(async () =>
            {
                while (!process.StandardOutput.EndOfStream)
                {
                    await process.StandardOutput.ReadLineAsync();
                }
            }, cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            if (cancellationToken.IsCancellationRequested)
                return new DownloadResult(false, "Conversion cancelled");

            if (process.ExitCode != 0)
            {
                var errorMsg = errorOutput.Count > 0
                    ? string.Join(Environment.NewLine, errorOutput.TakeLast(3))
                    : "ffmpeg conversion failed";
                return new DownloadResult(false, errorMsg);
            }

            return new DownloadResult(true, "Conversion completed");
        }

        /// <summary>
        /// Parses yt-dlp output to extract download percentage.
        /// </summary>
        private static double? ParseYtDlpProgress(string line)
        {
            // yt-dlp outputs lines like: [download]  45.2% of ~10.00MiB at  2.50MiB/s ETA 00:03
            var match = Regex.Match(line, @"\[download\]\s+([\d.]+)%");
            if (match.Success && double.TryParse(match.Groups[1].Value, out var percent))
                return percent;

            return null;
        }

        /// <summary>
        /// Searches the system PATH for an executable.
        /// </summary>
        private static string? FindInPath(string executable)
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv))
                return null;

            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                var fullPath = Path.Combine(dir.Trim(), executable);
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return null;
        }
    }

    /// <summary>
    /// Represents the result of a download operation.
    /// </summary>
    public record DownloadResult(bool Success, string Message);
}
