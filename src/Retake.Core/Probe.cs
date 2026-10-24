using System;
using System.Collections.Generic;
using System.IO;

namespace Retake.Core
{
    /// <summary>What <see cref="Probe.Read"/> found in a video container.</summary>
    public sealed class ProbeResult
    {
        /// <summary><c>mp4</c> (MP4 / MOV), <c>webm</c> (WebM / Matroska) or <c>unknown</c>.</summary>
        public string Container { get; set; } = "unknown";

        /// <summary>Frame rate of the first video track, or null.</summary>
        public double? Fps { get; set; }

        /// <summary>Duration in seconds, or null.</summary>
        public double? Duration { get; set; }

        /// <summary>Width of the first video track in pixels, or null.</summary>
        public int? Width { get; set; }

        /// <summary>Height of the first video track in pixels, or null.</summary>
        public int? Height { get; set; }

        /// <summary>Sample count of the video track when known (MP4 / MOV only), or null.</summary>
        public long? Frames { get; set; }
    }

    /// <summary>
    /// Container probe: frame rate, duration, size and frame count of MP4 / MOV and WebM / Matroska files. Reads box and
    /// element headers only and never decodes video. Damaged or foreign content gives container <c>unknown</c> with empty
    /// fields; only an unreadable file throws.
    /// </summary>
    public static class Probe
    {
        private const long MaxBlock = 256L * 1024 * 1024; // largest moov / Segment child read into memory
        private const long Unknown = -1;

        private sealed class ParseException : Exception
        {
            public ParseException(string message) : base(message) { }
        }

        private static bool IsParseError(Exception ex)
        {
            return ex is ParseException || ex is IndexOutOfRangeException || ex is ArgumentException || ex is OverflowException
                || ex is OutOfMemoryException || ex is InvalidCastException;
        }

        /// <summary>Probes a video file. Throws <see cref="IOException"/> when it cannot be opened or read.</summary>
        public static ProbeResult Read(string path)
        {
            FileStream handle;
            try
            {
                handle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new IOException("The file cannot be opened: " + ex.Message, ex);
            }
            using (handle)
            {
                byte[] head = ReadBytes(handle, 12);
                handle.Seek(0, SeekOrigin.Begin);
                if (head.Length >= 4 && head[0] == 0x1A && head[1] == 0x45 && head[2] == 0xDF && head[3] == 0xA3) return ProbeWebm(handle);
                return ProbeMp4(handle);
            }
        }

        private static byte[] ReadBytes(Stream stream, long count)
        {
            if (count <= 0) return new byte[0];
            if (stream.CanSeek) count = Math.Min(count, Math.Max(0, stream.Length - stream.Position));
            var buffer = new byte[count];
            int total = 0;
            while (total < count)
            {
                int n = stream.Read(buffer, total, (int)(count - total));
                if (n <= 0) break;
                total += n;
            }
            if (total == count) return buffer;
            var shorter = new byte[total];
            Array.Copy(buffer, shorter, total);
            return shorter;
        }

        // --- big-endian readers; they throw IndexOutOfRangeException like struct.unpack_from raises struct.error -----

        private static uint U32(byte[] buf, long offset)
        {
            if (offset < 0 || offset + 4 > buf.Length) throw new IndexOutOfRangeException();
            int o = (int)offset;
            return ((uint)buf[o] << 24) | ((uint)buf[o + 1] << 16) | ((uint)buf[o + 2] << 8) | buf[o + 3];
        }

        private static ulong U64(byte[] buf, long offset)
        {
            if (offset < 0 || offset + 8 > buf.Length) throw new IndexOutOfRangeException();
            return ((ulong)U32(buf, offset) << 32) | U32(buf, offset + 4);
        }

        // --- MP4 / MOV ---------------------------------------------------------------------------------------------

        private static bool IsBoxType(byte[] raw)
        {
            if (raw.Length != 4) return false;
            foreach (byte b in raw)
            {
                if (!((b >= 0x20 && b < 0x7F) || b == 0xA9)) return false;
            }
            return true;
        }

        private struct Box
        {
            public uint Type;
            public long PayloadStart;
            public long PayloadEnd;
        }

        private static uint Fourcc(string s)
        {
            return ((uint)s[0] << 24) | ((uint)s[1] << 16) | ((uint)s[2] << 8) | s[3];
        }

        private static IEnumerable<Box> IterBoxes(byte[] buf, long start, long end)
        {
            long pos = start;
            while (pos + 8 <= end)
            {
                ulong size = U32(buf, pos);
                uint kind = U32(buf, pos + 4);
                int header = 8;
                if (size == 1)
                {
                    if (pos + 16 > end) yield break;
                    size = U64(buf, pos + 8);
                    header = 16;
                }
                else if (size == 0)
                {
                    size = (ulong)(end - pos);
                }
                if (size < (ulong)header || size > (ulong)(end - pos)) yield break;
                yield return new Box { Type = kind, PayloadStart = pos + header, PayloadEnd = pos + (long)size };
                pos += (long)size;
            }
        }

        private static bool Find(byte[] buf, long start, long end, string kind, out long payloadStart, out long payloadEnd)
        {
            uint code = Fourcc(kind);
            foreach (Box box in IterBoxes(buf, start, end))
            {
                if (box.Type == code)
                {
                    payloadStart = box.PayloadStart;
                    payloadEnd = box.PayloadEnd;
                    return true;
                }
            }
            payloadStart = 0;
            payloadEnd = 0;
            return false;
        }

        private static ProbeResult ProbeMp4(FileStream handle)
        {
            var result = new ProbeResult();
            long sizeTotal = handle.Length;
            long pos = 0;
            bool seenFtyp = false;
            byte[]? moov = null;
            try
            {
                while (pos + 8 <= sizeTotal)
                {
                    handle.Seek(pos, SeekOrigin.Begin);
                    byte[] header = ReadBytes(handle, 16);
                    if (header.Length < 8) break;
                    ulong size = U32(header, 0);
                    var kindBytes = new[] { header[4], header[5], header[6], header[7] };
                    uint kind = U32(header, 4);
                    if (!IsBoxType(kindBytes)) break;
                    int headerLen = 8;
                    if (size == 1)
                    {
                        if (header.Length < 16) break;
                        size = U64(header, 8);
                        headerLen = 16;
                    }
                    else if (size == 0)
                    {
                        size = (ulong)(sizeTotal - pos);
                    }
                    if (size < (ulong)headerLen || size > (ulong)(sizeTotal - pos)) break;
                    if (kind == Fourcc("ftyp"))
                    {
                        seenFtyp = true;
                    }
                    else if (kind == Fourcc("moov"))
                    {
                        long payloadLen = (long)size - headerLen;
                        if (payloadLen > MaxBlock) break;
                        handle.Seek(pos + headerLen, SeekOrigin.Begin);
                        moov = ReadBytes(handle, payloadLen);
                        break;
                    }
                    pos += (long)size;
                }
            }
            catch (Exception ex) when (IsParseError(ex)) { }
            if (moov == null && !seenFtyp) return result;
            result.Container = "mp4";
            if (moov != null)
            {
                try { ReadMoov(moov, result); }
                catch (Exception ex) when (IsParseError(ex)) { }
            }
            return result;
        }

        private static void ReadMoov(byte[] moov, ProbeResult result)
        {
            double? movieDuration = null;
            long start, end;
            if (Find(moov, 0, moov.Length, "mvhd", out start, out end))
            {
                ulong timescale;
                ulong duration;
                if (moov[start] == 1)
                {
                    timescale = U32(moov, start + 20);
                    duration = U64(moov, start + 24);
                }
                else
                {
                    timescale = U32(moov, start + 12);
                    duration = U32(moov, start + 16);
                }
                if (timescale > 0) movieDuration = (double)duration / timescale;
            }
            double? trackDuration = null;
            uint trak = Fourcc("trak");
            foreach (Box track in IterBoxes(moov, 0, moov.Length))
            {
                if (track.Type != trak) continue;
                long mdiaStart, mdiaEnd;
                if (!Find(moov, track.PayloadStart, track.PayloadEnd, "mdia", out mdiaStart, out mdiaEnd)) continue;
                long hdlrStart, hdlrEnd;
                if (!Find(moov, mdiaStart, mdiaEnd, "hdlr", out hdlrStart, out hdlrEnd)) continue;
                if (!IsHandler(moov, hdlrStart + 8, "vide")) continue;
                long tkhdStart, tkhdEnd;
                if (Find(moov, track.PayloadStart, track.PayloadEnd, "tkhd", out tkhdStart, out tkhdEnd))
                {
                    long offset = tkhdStart + (moov[tkhdStart] == 1 ? 88 : 76);
                    uint width = U32(moov, offset);
                    uint height = U32(moov, offset + 4);
                    result.Width = (width >> 16) != 0 ? (int?)(int)(width >> 16) : null;
                    result.Height = (height >> 16) != 0 ? (int?)(int)(height >> 16) : null;
                }
                ulong mediaScale = 0;
                long mdhdStart, mdhdEnd;
                if (Find(moov, mdiaStart, mdiaEnd, "mdhd", out mdhdStart, out mdhdEnd))
                {
                    ulong mediaDuration;
                    if (moov[mdhdStart] == 1)
                    {
                        mediaScale = U32(moov, mdhdStart + 20);
                        mediaDuration = U64(moov, mdhdStart + 24);
                    }
                    else
                    {
                        mediaScale = U32(moov, mdhdStart + 12);
                        mediaDuration = U32(moov, mdhdStart + 16);
                    }
                    if (mediaScale > 0) trackDuration = (double)mediaDuration / mediaScale;
                }
                long minfStart, minfEnd, stblStart = 0, stblEnd = 0, sttsStart, sttsEnd;
                bool hasStbl = Find(moov, mdiaStart, mdiaEnd, "minf", out minfStart, out minfEnd)
                    && Find(moov, minfStart, minfEnd, "stbl", out stblStart, out stblEnd);
                if (hasStbl && Find(moov, stblStart, stblEnd, "stts", out sttsStart, out sttsEnd) && mediaScale > 0)
                    ReadStts(moov, sttsStart, sttsEnd, mediaScale, result);
                break;
            }
            if (movieDuration.HasValue && movieDuration.Value > 0) result.Duration = movieDuration;
            else if (trackDuration.HasValue && trackDuration.Value > 0) result.Duration = trackDuration;
            else if (result.Frames.HasValue && result.Frames.Value != 0 && result.Fps.HasValue && result.Fps.Value != 0)
                result.Duration = result.Frames.Value / result.Fps.Value;
        }

        private static bool IsHandler(byte[] buf, long offset, string handler)
        {
            if (offset < 0 || offset + 4 > buf.Length) return false;
            for (int i = 0; i < 4; i++)
            {
                if (buf[offset + i] != handler[i]) return false;
            }
            return true;
        }

        private static void ReadStts(byte[] buf, long start, long end, ulong timescale, ProbeResult result)
        {
            long count = U32(buf, start + 4);
            count = Math.Min(count, (end - start - 8) / 8);
            var deltas = new List<uint>();
            var totals = new Dictionary<uint, long>();
            long frames = 0;
            for (long i = 0; i < count; i++)
            {
                uint samples = U32(buf, start + 8 + 8 * i);
                uint delta = U32(buf, start + 12 + 8 * i);
                frames += samples;
                long existing;
                if (totals.TryGetValue(delta, out existing)) totals[delta] = existing + samples;
                else { totals[delta] = samples; deltas.Add(delta); }
            }
            if (frames > 0) result.Frames = frames;
            uint best = 0;
            long bestTotal = -1;
            foreach (uint delta in deltas)
            {
                if (totals[delta] > bestTotal) { best = delta; bestTotal = totals[delta]; }
            }
            if (best > 0) result.Fps = (double)timescale / best;
        }

        // --- WebM / Matroska ---------------------------------------------------------------------------------------

        private const long IdSegment = 0x18538067;
        private const long IdInfo = 0x1549A966;
        private const long IdTracks = 0x1654AE6B;
        private const long IdCluster = 0x1F43B675;
        private const long IdTimecodeScale = 0x2AD7B1;
        private const long IdDuration = 0x4489;
        private const long IdTrackEntry = 0xAE;
        private const long IdTrackType = 0x83;
        private const long IdDefaultDuration = 0x23E383;
        private const long IdVideo = 0xE0;
        private const long IdPixelWidth = 0xB0;
        private const long IdPixelHeight = 0xBA;

        /// <summary>Source of bytes for the element reader: a file or an in-memory element payload.</summary>
        private abstract class ByteSource
        {
            public abstract byte[] Read(int count);
        }

        private sealed class StreamSource : ByteSource
        {
            private readonly Stream _stream;
            public StreamSource(Stream stream) { _stream = stream; }
            public override byte[] Read(int count) { return ReadBytes(_stream, count); }
        }

        private sealed class MemorySource : ByteSource
        {
            private readonly byte[] _buf;
            public int Pos;
            public MemorySource(byte[] buf) { _buf = buf; }
            public int Length { get { return _buf.Length; } }
            public override byte[] Read(int count)
            {
                int n = Math.Max(0, Math.Min(count, _buf.Length - Pos));
                var chunk = new byte[n];
                Array.Copy(_buf, Pos, chunk, 0, n);
                Pos += n;
                return chunk;
            }
        }

        private struct ElementHeader
        {
            public long Id;
            public long Size; // Unknown (-1) when the size is all ones
        }

        /// <summary>Reads (id, payload size) with the source; null at end of data. Throws <see cref="ParseException"/> for an invalid id or size.</summary>
        private static ElementHeader? ReadElementHeader(ByteSource source)
        {
            byte[] first = source.Read(1);
            if (first.Length == 0) return null;
            int lead = first[0];
            int idLen = 0;
            for (int bit = 0; bit < 4; bit++)
            {
                if ((lead & (0x80 >> bit)) != 0) { idLen = bit + 1; break; }
            }
            if (idLen == 0) throw new ParseException("bad element id");
            byte[] rest = source.Read(idLen - 1);
            if (rest.Length != idLen - 1) return null;
            long id = first[0];
            foreach (byte b in rest) id = (id << 8) | b;
            byte[] sizeFirst = source.Read(1);
            if (sizeFirst.Length == 0) return null;
            lead = sizeFirst[0];
            int sizeLen = 0;
            for (int bit = 0; bit < 8; bit++)
            {
                if ((lead & (0x80 >> bit)) != 0) { sizeLen = bit + 1; break; }
            }
            if (sizeLen == 0) throw new ParseException("bad element size");
            byte[] sizeRest = source.Read(sizeLen - 1);
            if (sizeRest.Length != sizeLen - 1) return null;
            long value = lead & (0xFF >> sizeLen);
            foreach (byte b in sizeRest) value = (value << 8) | b;
            bool unknown = value == (1L << (7 * sizeLen)) - 1;
            return new ElementHeader { Id = id, Size = unknown ? Unknown : value };
        }

        private struct Element
        {
            public long Id;
            public byte[] Payload;
        }

        /// <summary>The (id, payload) pairs of an in-memory master element payload.</summary>
        private static IEnumerable<Element> IterElements(byte[] buf)
        {
            var source = new MemorySource(buf);
            while (source.Pos < source.Length)
            {
                ElementHeader? header = ReadElementHeader(source);
                if (header == null) yield break;
                long size = header.Value.Size;
                if (size == Unknown || source.Pos + size > source.Length) yield break;
                var payload = new byte[size];
                Array.Copy(buf, source.Pos, payload, 0, (int)size);
                source.Pos += (int)size;
                yield return new Element { Id = header.Value.Id, Payload = payload };
            }
        }

        private static ulong UInt(byte[] payload)
        {
            ulong value = 0;
            foreach (byte b in payload) value = checked((value << 8) | b);
            return value;
        }

        private static double Float(byte[] payload)
        {
            if (payload.Length == 4)
            {
                var bytes = new[] { payload[3], payload[2], payload[1], payload[0] };
                return BitConverter.ToSingle(bytes, 0);
            }
            if (payload.Length == 8)
            {
                var bytes = new byte[8];
                for (int i = 0; i < 8; i++) bytes[i] = payload[7 - i];
                return BitConverter.ToDouble(bytes, 0);
            }
            throw new ParseException("bad float size");
        }

        private static ProbeResult ProbeWebm(FileStream handle)
        {
            var result = new ProbeResult { Container = "webm" };
            ulong scale = 1000000;
            double? durationUnits = null;
            try
            {
                var source = new StreamSource(handle);
                while (true)
                {
                    ElementHeader? header = ReadElementHeader(source);
                    if (header == null) break;
                    long id = header.Value.Id;
                    long size = header.Value.Size;
                    if (id == IdSegment) break; // descend: the children follow directly
                    if (size == Unknown) return result;
                    handle.Seek(size, SeekOrigin.Current);
                }
                while (true)
                {
                    ElementHeader? header = ReadElementHeader(source);
                    if (header == null) break;
                    long id = header.Value.Id;
                    long size = header.Value.Size;
                    if (id == IdCluster || size == Unknown) break;
                    if (id == IdInfo || id == IdTracks)
                    {
                        if (size > MaxBlock) break;
                        byte[] payload = ReadBytes(handle, size);
                        if (payload.Length != size) break;
                        if (id == IdInfo)
                        {
                            foreach (Element child in IterElements(payload))
                            {
                                if (child.Id == IdTimecodeScale)
                                {
                                    ulong value = UInt(child.Payload);
                                    if (value != 0) scale = value;
                                }
                                else if (child.Id == IdDuration)
                                {
                                    durationUnits = Float(child.Payload);
                                }
                            }
                        }
                        else
                        {
                            ReadTracks(payload, result);
                        }
                    }
                    else
                    {
                        handle.Seek(size, SeekOrigin.Current);
                    }
                }
            }
            catch (Exception ex) when (IsParseError(ex)) { }
            if (durationUnits.HasValue && durationUnits.Value > 0) result.Duration = durationUnits.Value * scale / 1e9;
            return result;
        }

        private static void ReadTracks(byte[] payload, ProbeResult result)
        {
            foreach (Element element in IterElements(payload))
            {
                if (element.Id != IdTrackEntry) continue;
                ulong? trackType = null;
                ulong defaultDuration = 0;
                ulong width = 0;
                ulong height = 0;
                foreach (Element child in IterElements(element.Payload))
                {
                    if (child.Id == IdTrackType) trackType = UInt(child.Payload);
                    else if (child.Id == IdDefaultDuration) defaultDuration = UInt(child.Payload);
                    else if (child.Id == IdVideo)
                    {
                        foreach (Element video in IterElements(child.Payload))
                        {
                            if (video.Id == IdPixelWidth) width = UInt(video.Payload);
                            else if (video.Id == IdPixelHeight) height = UInt(video.Payload);
                        }
                    }
                }
                if (trackType == 1)
                {
                    if (defaultDuration > 0) result.Fps = 1e9 / defaultDuration;
                    result.Width = width != 0 ? (int?)checked((int)width) : null;
                    result.Height = height != 0 ? (int?)checked((int)height) : null;
                    return;
                }
            }
        }
    }
}
