using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Retake.Core;

namespace Retake.Tests
{
    /// <summary>Builds MP4 and WebM files in memory (the port of the writers in tests/test_core_probe.py).</summary>
    public static class Media
    {
        private static void Be(List<byte> list, ulong value, int bytes)
        {
            for (int i = bytes - 1; i >= 0; i--) list.Add((byte)(value >> (8 * i)));
        }

        private static byte[] Cat(params byte[][] parts)
        {
            return parts.SelectMany(p => p).ToArray();
        }

        private static byte[] Ascii(string s) { return s.Select(c => (byte)c).ToArray(); }

        private static byte[] Zeros(int n) { return new byte[n]; }

        public static byte[] Box(string kind, byte[] payload, bool large = false)
        {
            var list = new List<byte>();
            if (large)
            {
                Be(list, 1, 4);
                list.AddRange(Ascii(kind));
                Be(list, (ulong)(16 + payload.Length), 8);
            }
            else
            {
                Be(list, (ulong)(8 + payload.Length), 4);
                list.AddRange(Ascii(kind));
            }
            list.AddRange(payload);
            return list.ToArray();
        }

        private static byte[] Full(int version, byte[] body)
        {
            return Cat(new byte[] { (byte)version, 0, 0, 0 }, body);
        }

        private static byte[] Header(int version, uint timescale, ulong duration, int tail)
        {
            var list = new List<byte>();
            if (version == 1)
            {
                Be(list, 0, 8); Be(list, 0, 8); Be(list, timescale, 4); Be(list, duration, 8);
            }
            else
            {
                Be(list, 0, 4); Be(list, 0, 4); Be(list, timescale, 4); Be(list, duration, 4);
            }
            list.AddRange(Zeros(tail));
            return list.ToArray();
        }

        public static byte[] Mvhd(uint timescale, ulong duration, int version = 0)
        {
            return Box("mvhd", Full(version, Header(version, timescale, duration, 80)));
        }

        public static byte[] Tkhd(uint width, uint height, int version = 0)
        {
            var head = new List<byte>();
            if (version == 1)
            {
                Be(head, 0, 8); Be(head, 0, 8); Be(head, 1, 4); Be(head, 0, 4); Be(head, 0, 8);
            }
            else
            {
                for (int i = 0; i < 5; i++) Be(head, i == 2 ? 1UL : 0UL, 4);
            }
            var size = new List<byte>();
            Be(size, (ulong)width << 16, 4);
            Be(size, (ulong)height << 16, 4);
            return Box("tkhd", Full(version, Cat(head.ToArray(), Zeros(8), Zeros(8), Zeros(36), size.ToArray())));
        }

        public static byte[] Mdhd(uint timescale, ulong duration, int version = 0)
        {
            return Box("mdhd", Full(version, Header(version, timescale, duration, 4)));
        }

        public static byte[] Hdlr(string handler)
        {
            var head = new List<byte>();
            Be(head, 0, 4);
            head.AddRange(Ascii(handler));
            return Box("hdlr", Full(0, Cat(head.ToArray(), Zeros(12), Ascii("name"), new byte[] { 0 })));
        }

        public static byte[] Stts(IList<KeyValuePair<uint, uint>> entries)
        {
            var body = new List<byte>();
            Be(body, (ulong)entries.Count, 4);
            foreach (KeyValuePair<uint, uint> e in entries) { Be(body, e.Key, 4); Be(body, e.Value, 4); }
            return Box("stts", Full(0, body.ToArray()));
        }

        public static byte[] Trak(string handler, uint width, uint height, uint timescale, ulong duration,
            IList<KeyValuePair<uint, uint>> entries, int version = 0)
        {
            var stsd = new List<byte>();
            Be(stsd, 0, 4);
            byte[] stbl = Box("stbl", Cat(Box("stsd", Full(0, stsd.ToArray())), Stts(entries)));
            byte[] minf = Box("minf", stbl);
            byte[] mdia = Box("mdia", Cat(Mdhd(timescale, duration, version), Hdlr(handler), minf));
            return Box("trak", Cat(Tkhd(width, height, version), mdia));
        }

        public static KeyValuePair<uint, uint> E(uint count, uint delta) { return new KeyValuePair<uint, uint>(count, delta); }

        public static byte[] BuildMp4(IList<KeyValuePair<uint, uint>> videoEntries, uint videoTimescale, uint movieTimescale = 1000,
            ulong movieDuration = 0, uint width = 1920, uint height = 1080, int version = 0,
            bool moovLast = false, bool bigMdat = false, bool mdatToEnd = false)
        {
            ulong videoDuration = 0;
            foreach (KeyValuePair<uint, uint> e in videoEntries) videoDuration += (ulong)e.Key * e.Value;
            byte[] audio = Trak("soun", 0, 0, 48000, 48000UL * 3, new[] { E(144, 1024) }, version);
            byte[] video = Trak("vide", width, height, videoTimescale, videoDuration, videoEntries, version);
            byte[] moov = Box("moov", Cat(Mvhd(movieTimescale, movieDuration, version), audio, video));
            byte[] ftyp = Box("ftyp", Cat(Ascii("isom"), new byte[] { 0, 0, 2, 0 }, Ascii("isomavc1")));
            byte[] payload = Zeros(64);
            byte[] mdat;
            if (mdatToEnd)
            {
                var head = new List<byte>();
                Be(head, 0, 4);
                head.AddRange(Ascii("mdat"));
                mdat = Cat(head.ToArray(), payload);
            }
            else
            {
                mdat = Box("mdat", payload, bigMdat);
            }
            return moovLast ? Cat(ftyp, mdat, moov) : Cat(ftyp, moov, mdat);
        }

        // --- WebM --------------------------------------------------------------------------------------------------

        private static byte[] EbmlId(ulong value)
        {
            int length = 1;
            while ((value >> (8 * length)) != 0) length++;
            var list = new List<byte>();
            Be(list, value, length);
            return list.ToArray();
        }

        private static byte[] EbmlSize(long? size)
        {
            if (size == null) return new byte[] { 0x01, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff };
            for (int length = 1; length <= 8; length++)
            {
                if (size.Value < (1L << (7 * length)) - 1)
                {
                    var list = new List<byte>();
                    Be(list, (ulong)size.Value | (1UL << (7 * length)), length);
                    return list.ToArray();
                }
            }
            throw new InvalidOperationException();
        }

        public static byte[] El(ulong id, byte[] payload, bool unknownSize = false)
        {
            return Cat(EbmlId(id), EbmlSize(unknownSize ? (long?)null : payload.Length), payload);
        }

        public static byte[] UInt(ulong value)
        {
            int length = 1;
            while ((value >> (8 * length)) != 0 && length < 8) length++;
            var list = new List<byte>();
            Be(list, value, length);
            return list.ToArray();
        }

        public static byte[] Double(double value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Array.Reverse(bytes);
            return bytes;
        }

        public static byte[] BuildWebm(double? fps, uint width, uint height, double? durationMs = 3000.0, ulong? timescale = 1000000)
        {
            byte[] header = El(0x1A45DFA3, El(0x4282, Ascii("webm")));
            byte[] infoChildren = new byte[0];
            if (timescale != null) infoChildren = Cat(infoChildren, El(0x2AD7B1, UInt(timescale.Value)));
            if (durationMs != null) infoChildren = Cat(infoChildren, El(0x4489, Double(durationMs.Value)));
            byte[] info = El(0x1549A966, infoChildren);
            byte[] audioEntry = El(0xAE, Cat(El(0xD7, UInt(1)), El(0x83, UInt(2)), El(0x23E383, UInt(20000000))));
            byte[] videoChildren = Cat(El(0xD7, UInt(2)), El(0x83, UInt(1)));
            if (fps != null) videoChildren = Cat(videoChildren, El(0x23E383, UInt((ulong)Math.Round(1e9 / fps.Value, MidpointRounding.ToEven))));
            videoChildren = Cat(videoChildren, El(0xE0, Cat(El(0xB0, UInt(width)), El(0xBA, UInt(height)))));
            byte[] tracks = El(0x1654AE6B, Cat(audioEntry, El(0xAE, videoChildren)));
            // A Cluster carries bogus data that must never be read.
            byte[] cluster = El(0x1F43B675, Cat(El(0xE7, UInt(0)), El(0x1549A966, El(0x4489, Double(1.0)))));
            return Cat(header, El(0x18538067, Cat(info, tracks, cluster), true));
        }
    }

    /// <summary>Port of tests/test_core_probe.py.</summary>
    public static class ProbeTests
    {
        private static string Write(TestEnv t, string name, byte[] data)
        {
            string path = t.Combine(name);
            File.WriteAllBytes(path, data);
            return path;
        }

        private static KeyValuePair<uint, uint>[] V(params uint[] pairs)
        {
            var list = new List<KeyValuePair<uint, uint>>();
            for (int i = 0; i < pairs.Length; i += 2) list.Add(Media.E(pairs[i], pairs[i + 1]));
            return list.ToArray();
        }

        [Test("test_core_probe.Mp4Test.test_25_fps")]
        public static void Mp4_25Fps(TestEnv t)
        {
            ProbeResult r = Probe.Read(Write(t, "a.mp4", Media.BuildMp4(V(2310, 512), 12800, 1000, 92400)));
            Check.Equal("mp4", r.Container);
            Check.Near(25.0, r.Fps ?? 0);
            Check.Equal(1920, r.Width);
            Check.Equal(1080, r.Height);
            Check.Equal(2310L, r.Frames);
            Check.Near(92.4, r.Duration ?? 0);
        }

        [Test("test_core_probe.Mp4Test.test_29_97_from_stts_delta_1001")]
        public static void Mp4_2997(TestEnv t)
        {
            ProbeResult r = Probe.Read(Write(t, "a.mp4", Media.BuildMp4(V(900, 1001), 30000, movieDuration: 30030)));
            Check.Near(30000.0 / 1001.0, r.Fps ?? 0);
            Check.Equal(29.97, Timecode.NominalRate(r.Fps ?? 0));
            Check.Equal(900L, r.Frames);
        }

        [Test("test_core_probe.Mp4Test.test_23_976")]
        public static void Mp4_23976(TestEnv t)
        {
            ProbeResult r = Probe.Read(Write(t, "a.mp4", Media.BuildMp4(V(240, 1001), 24000, movieDuration: 10010, width: 1280, height: 720)));
            Check.Equal(23.976, Timecode.NominalRate(r.Fps ?? 0));
            Check.Equal(1280, r.Width);
            Check.Equal(720, r.Height);
        }

        [Test("test_core_probe.Mp4Test.test_most_frequent_delta_wins_and_frames_are_summed")]
        public static void Mp4_MostFrequentDeltaWins(TestEnv t)
        {
            ProbeResult r = Probe.Read(Write(t, "a.mp4", Media.BuildMp4(V(1, 1000, 100, 512, 3, 700), 12800)));
            Check.Near(25.0, r.Fps ?? 0);
            Check.Equal(104L, r.Frames);
        }

        [Test("test_core_probe.Mp4Test.test_audio_track_before_video_is_skipped")]
        public static void Mp4_AudioTrackBeforeVideoIsSkipped(TestEnv t)
        {
            ProbeResult r = Probe.Read(Write(t, "a.mp4", Media.BuildMp4(V(100, 512), 12800)));
            Check.Near(25.0, r.Fps ?? 0);
            Check.Equal(100L, r.Frames); // the audio track has 144 samples
        }

        [Test("test_core_probe.Mp4Test.test_version_1_boxes")]
        public static void Mp4_Version1Boxes(TestEnv t)
        {
            ProbeResult r = Probe.Read(Write(t, "a.mp4", Media.BuildMp4(V(100, 512), 12800, movieDuration: 4000, version: 1)));
            Check.Near(25.0, r.Fps ?? 0);
            Check.Equal(1920, r.Width);
            Check.Equal(1080, r.Height);
            Check.Near(4.0, r.Duration ?? 0);
        }

        [Test("test_core_probe.Mp4Test.test_moov_after_mdat_with_64_bit_size")]
        public static void Mp4_MoovAfterMdatWith64BitSize(TestEnv t)
        {
            ProbeResult r = Probe.Read(Write(t, "a.mp4", Media.BuildMp4(V(100, 512), 12800, moovLast: true, bigMdat: true)));
            Check.Equal("mp4", r.Container);
            Check.Near(25.0, r.Fps ?? 0);
        }

        [Test("test_core_probe.Mp4Test.test_mdat_running_to_end_of_file")]
        public static void Mp4_MdatRunningToEndOfFile(TestEnv t)
        {
            // moov in front, mdat with size 0 behind it
            ProbeResult r = Probe.Read(Write(t, "a.mp4", Media.BuildMp4(V(100, 512), 12800, mdatToEnd: true)));
            Check.Near(25.0, r.Fps ?? 0);
        }

        [Test("test_core_probe.Mp4Test.test_duration_falls_back_to_the_track")]
        public static void Mp4_DurationFallsBackToTheTrack(TestEnv t)
        {
            ProbeResult r = Probe.Read(Write(t, "a.mp4", Media.BuildMp4(V(100, 512), 12800, movieDuration: 0)));
            Check.Near(4.0, r.Duration ?? 0);
        }

        [Test("test_core_probe.Mp4Test.test_truncated_file_does_not_raise")]
        public static void Mp4_TruncatedFileDoesNotRaise(TestEnv t)
        {
            byte[] data = Media.BuildMp4(V(100, 512), 12800);
            foreach (int cut in new[] { 3, 8, 20, 60, data.Length - 100 })
            {
                ProbeResult r = Probe.Read(Write(t, "cut.mp4", data.Take(cut).ToArray()));
                Check.True(r.Container == "mp4" || r.Container == "unknown", "cut " + cut + " gave " + r.Container);
            }
        }

        [Test("test_core_probe.WebmTest.test_25_fps")]
        public static void Webm_25Fps(TestEnv t)
        {
            ProbeResult r = Probe.Read(Write(t, "a.webm", Media.BuildWebm(25.0, 1920, 1080, 3000.0)));
            Check.Equal("webm", r.Container);
            Check.Near(25.0, r.Fps ?? 0);
            Check.Equal(1920, r.Width);
            Check.Equal(1080, r.Height);
            Check.Near(3.0, r.Duration ?? 0);
            Check.Null(r.Frames);
        }

        [Test("test_core_probe.WebmTest.test_29_97_and_23_976")]
        public static void Webm_2997And23976(TestEnv t)
        {
            ProbeResult r = Probe.Read(Write(t, "a.webm", Media.BuildWebm(30000.0 / 1001.0, 640, 360)));
            Check.Equal(29.97, Timecode.NominalRate(r.Fps ?? 0));
            r = Probe.Read(Write(t, "b.webm", Media.BuildWebm(24000.0 / 1001.0, 640, 360)));
            Check.Equal(23.976, Timecode.NominalRate(r.Fps ?? 0));
        }

        [Test("test_core_probe.WebmTest.test_timecode_scale")]
        public static void Webm_TimecodeScale(TestEnv t)
        {
            ProbeResult r = Probe.Read(Write(t, "a.webm", Media.BuildWebm(25.0, 320, 240, 5000.0, 500000)));
            Check.Near(2.5, r.Duration ?? 0);
        }

        [Test("test_core_probe.WebmTest.test_missing_fields")]
        public static void Webm_MissingFields(TestEnv t)
        {
            ProbeResult r = Probe.Read(Write(t, "a.webm", Media.BuildWebm(null, 320, 240, null, null)));
            Check.Equal("webm", r.Container);
            Check.Null(r.Fps);
            Check.Null(r.Duration);
            Check.Equal(320, r.Width);
            Check.Equal(240, r.Height);
        }

        [Test("test_core_probe.WebmTest.test_truncated_file_does_not_raise")]
        public static void Webm_TruncatedFileDoesNotRaise(TestEnv t)
        {
            byte[] data = Media.BuildWebm(25.0, 1920, 1080);
            foreach (int cut in new[] { 4, 5, 20, 40, 70 })
            {
                ProbeResult r = Probe.Read(Write(t, "cut.webm", data.Take(cut).ToArray()));
                Check.Equal("webm", r.Container);
            }
        }

        [Test("test_core_probe.UnknownTest.test_random_bytes")]
        public static void Unknown_RandomBytes(TestEnv t)
        {
            var rng = new Random(7);
            for (int i = 0; i < 20; i++)
            {
                var bytes = new byte[4096];
                rng.NextBytes(bytes);
                ProbeResult r = Probe.Read(Write(t, "r" + i + ".bin", bytes));
                Check.Equal("unknown", r.Container);
                Check.Null(r.Fps);
                Check.Null(r.Duration);
                Check.Null(r.Width);
                Check.Null(r.Height);
                Check.Null(r.Frames);
            }
        }

        [Test("test_core_probe.UnknownTest.test_empty_and_text_files")]
        public static void Unknown_EmptyAndTextFiles(TestEnv t)
        {
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < 10; i++) text.Append("hello world, this is not a video\n");
            foreach (KeyValuePair<string, byte[]> item in new[]
            {
                new KeyValuePair<string, byte[]>("e.bin", new byte[0]),
                new KeyValuePair<string, byte[]>("t.txt", System.Text.Encoding.ASCII.GetBytes(text.ToString())),
            })
            {
                ProbeResult r = Probe.Read(Write(t, item.Key, item.Value));
                Check.Equal("unknown", r.Container);
                Check.Null(r.Fps);
            }
        }

        [Test("test_core_probe.UnknownTest.test_missing_file_raises_oserror")]
        public static void Unknown_MissingFileRaises(TestEnv t)
        {
            Check.Throws<IOException>(() => Probe.Read(t.Combine("nope.mp4")));
        }

        [Test("test_core_probe.UnknownTest.test_directory_raises_oserror")]
        public static void Unknown_DirectoryRaises(TestEnv t)
        {
            Check.Throws<IOException>(() => Probe.Read(t.Tmp));
        }
    }
}
