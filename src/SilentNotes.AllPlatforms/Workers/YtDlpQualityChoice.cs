// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;

namespace SilentNotes.Workers
{
    /// <summary>Media mode for a yt-dlp download.</summary>
    public enum YtDlpMediaMode
    {
        /// <summary>Muxed video + audio (default).</summary>
        VideoWithAudio = 0,

        /// <summary>Extract audio only.</summary>
        AudioOnly = 1,

        /// <summary>Video stream without audio.</summary>
        VideoOnly = 2,
    }

    /// <summary>One selectable quality preset shown in Player / URLCheck dialogs.</summary>
    public sealed class YtDlpQualityChoice
    {
        public YtDlpQualityChoice(string label, YtDlpMediaMode mode, int? maxHeight = null, int? maxAudioBitrateKbps = null)
        {
            Label = label ?? string.Empty;
            Mode = mode;
            MaxHeight = maxHeight;
            MaxAudioBitrateKbps = maxAudioBitrateKbps;
        }

        public string Label { get; }

        public YtDlpMediaMode Mode { get; }

        /// <summary>Max video height in pixels, or null for best available.</summary>
        public int? MaxHeight { get; }

        /// <summary>Max audio bitrate in kbps, or null for best available.</summary>
        public int? MaxAudioBitrateKbps { get; }

        /// <summary>Default list: 360p first, then higher video, audio-only, and video-only options.</summary>
        public static IReadOnlyList<YtDlpQualityChoice> DefaultChoices { get; } = new[]
        {
            new YtDlpQualityChoice("360p (default)", YtDlpMediaMode.VideoWithAudio, maxHeight: 360),
            new YtDlpQualityChoice("480p", YtDlpMediaMode.VideoWithAudio, maxHeight: 480),
            new YtDlpQualityChoice("720p", YtDlpMediaMode.VideoWithAudio, maxHeight: 720),
            new YtDlpQualityChoice("1080p", YtDlpMediaMode.VideoWithAudio, maxHeight: 1080),
            new YtDlpQualityChoice("Best video", YtDlpMediaMode.VideoWithAudio),
            new YtDlpQualityChoice("Audio only (best)", YtDlpMediaMode.AudioOnly),
            new YtDlpQualityChoice("Audio only (128 kbps)", YtDlpMediaMode.AudioOnly, maxAudioBitrateKbps: 128),
            new YtDlpQualityChoice("Audio only (64 kbps)", YtDlpMediaMode.AudioOnly, maxAudioBitrateKbps: 64),
            new YtDlpQualityChoice("Video only 360p (no audio)", YtDlpMediaMode.VideoOnly, maxHeight: 360),
            new YtDlpQualityChoice("Video only best (no audio)", YtDlpMediaMode.VideoOnly),
        };

        /// <summary>Builds yt-dlp CLI arguments for this choice (without binary / URL).</summary>
        public IReadOnlyList<string> BuildArguments(string outputDirectory, string ffmpegPath)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("Output directory is required.", nameof(outputDirectory));

            var args = new List<string>
            {
                "--newline",
                "--no-playlist",
                "-o",
                Path.Combine(outputDirectory, "%(title).180B [%(id)s].%(ext)s"),
            };

            if (!string.IsNullOrWhiteSpace(ffmpegPath) && File.Exists(ffmpegPath))
            {
                args.Add("--ffmpeg-location");
                args.Add(ffmpegPath);
            }

            switch (Mode)
            {
                case YtDlpMediaMode.AudioOnly:
                    args.Add("-x");
                    args.Add("--audio-format");
                    args.Add("m4a");
                    args.Add("-f");
                    args.Add(BuildAudioFormatSelector());
                    break;

                case YtDlpMediaMode.VideoOnly:
                    args.Add("-f");
                    args.Add(BuildVideoOnlyFormatSelector());
                    args.Add("--merge-output-format");
                    args.Add("mp4");
                    break;

                default:
                    args.Add("-f");
                    args.Add(BuildVideoWithAudioFormatSelector());
                    args.Add("--merge-output-format");
                    args.Add("mp4");
                    break;
            }

            return args;
        }

        private string BuildVideoWithAudioFormatSelector()
        {
            if (MaxHeight is int h && h > 0)
            {
                return string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "bv*[height<=?{0}]+ba/b[height<=?{0}]/bv*+ba/b",
                    h);
            }

            return "bv*+ba/b";
        }

        private string BuildVideoOnlyFormatSelector()
        {
            if (MaxHeight is int h && h > 0)
            {
                return string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "bv*[height<=?{0}]/b[height<=?{0}]/bv*/b",
                    h);
            }

            return "bv*/b";
        }

        private string BuildAudioFormatSelector()
        {
            if (MaxAudioBitrateKbps is int abr && abr > 0)
            {
                return string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "ba[abr<=?{0}]/ba/b",
                    abr);
            }

            return "ba/b";
        }
    }
}
