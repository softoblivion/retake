using System;
using System.Collections.Generic;
using Retake.Core;

namespace Retake.Tests
{
    /// <summary>Timecodes: the reference values of docs/CORE.md; the test names follow the 1.x suite they match.</summary>
    public static class TimecodeTests
    {
        private static readonly object[][] Table =
        {
            new object[] { 25.0, 0, "0:00:00:00" },
            new object[] { 25.0, 307, "0:00:12:07" },
            new object[] { 25.0, 90000, "1:00:00:00" },
            new object[] { 23.976, 24, "0:00:01:00" },
            new object[] { 29.97, 1799, "0;00;59;29" },
            new object[] { 29.97, 1800, "0;01;00;02" },
            new object[] { 29.97, 17982, "0;10;00;00" },
            new object[] { 29.97, 107892, "1;00;00;00" },
            new object[] { 59.94, 3600, "0;01;00;04" },
            new object[] { 120.0, 125, "0:00:01:005" },
        };

        [Test("test_core_timecode.TableTest.test_frame_to_timecode_rows")]
        public static void FrameToTimecodeRows(TestEnv t)
        {
            foreach (object[] row in Table)
            {
                Check.Equal((string)row[2], Timecode.FrameToTimecode((int)row[1], Timecode.BaseFor((double)row[0])), "fps " + row[0] + " frame " + row[1]);
            }
        }

        [Test("test_core_timecode.TableTest.test_timecode_to_frame_rows")]
        public static void TimecodeToFrameRows(TestEnv t)
        {
            foreach (object[] row in Table)
            {
                Check.Equal((int)row[1], Timecode.TimecodeToFrame((string)row[2], Timecode.BaseFor((double)row[0])), "fps " + row[0] + " text " + row[2]);
            }
        }

        [Test("test_core_timecode.TableTest.test_round_trip_every_frame")]
        public static void RoundTripEveryFrame(TestEnv t)
        {
            foreach (double fps in new[] { 25.0, 23.976, 29.97, 59.94, 30.0, 120.0 })
            {
                TimecodeBase tb = Timecode.BaseFor(fps);
                for (int frame = 0; frame < 130000; frame += 7)
                {
                    string text = Timecode.FrameToTimecode(frame, tb);
                    Check.Equal(frame, Timecode.TimecodeToFrame(text, tb), fps + " " + frame + " " + text);
                }
            }
        }

        [Test("test_core_timecode.TableTest.test_drop_frame_numbers_are_continuous")]
        public static void DropFrameNumbersAreContinuous(TestEnv t)
        {
            TimecodeBase tb = Timecode.BaseFor(29.97);
            var seen = new List<string>();
            for (int n = 0; n < 40000; n++) seen.Add(Timecode.FrameToTimecode(n, tb));
            Check.Equal(seen.Count, new HashSet<string>(seen).Count);
            Check.Equal("0;00;59;29", seen[1799]);
            Check.Equal("0;01;00;02", seen[1800]);
            Check.False(seen.Contains("0;01;00;00"));
            Check.False(seen.Contains("0;01;00;01"));
            Check.True(seen.Contains("0;10;00;00"));
        }

        [Test("test_core_timecode.BaseTest.test_base_fields")]
        public static void BaseFields(TestEnv t)
        {
            TimecodeBase tb = Timecode.BaseFor(29.97);
            Check.Equal(30, tb.Base);
            Check.Equal(true, tb.Drop);
            Check.Equal(2, tb.Digits);
            Check.Equal(";", tb.Separator);
            Check.Near(30000.0 / 1001.0, tb.Fps, 9);
            tb = Timecode.BaseFor(59.94);
            Check.Equal(60, tb.Base);
            Check.Equal(true, tb.Drop);
            Check.Equal(";", tb.Separator);
            Check.Near(60000.0 / 1001.0, tb.Fps, 9);
            tb = Timecode.BaseFor(25);
            Check.Equal(25, tb.Base);
            Check.Equal(false, tb.Drop);
            Check.Equal(2, tb.Digits);
            Check.Equal(":", tb.Separator);
            Check.Equal(25.0, tb.Fps);
            tb = Timecode.BaseFor(120);
            Check.Equal(120, tb.Base);
            Check.Equal(3, tb.Digits);
        }

        [Test("test_core_timecode.BaseTest.test_23_976_variants_share_one_base")]
        public static void Variants23976ShareOneBase(TestEnv t)
        {
            foreach (double value in new[] { 23.98, 23.976, 24000.0 / 1001.0 })
            {
                Check.Equal(23.976, Timecode.NominalRate(value));
                TimecodeBase tb = Timecode.BaseFor(value);
                Check.Equal(24, tb.Base);
                Check.Equal(false, tb.Drop);
                Check.Equal(24000.0 / 1001.0, tb.Fps);
            }
        }

        [Test("test_core_timecode.BaseTest.test_nominal_rate_snapping")]
        public static void NominalRateSnapping(TestEnv t)
        {
            Check.Equal(29.97, Timecode.NominalRate(30000.0 / 1001.0));
            Check.Equal(29.97, Timecode.NominalRate(29.97));
            Check.Equal(30.0, Timecode.NominalRate(30.0));
            Check.Equal(25.0, Timecode.NominalRate(25.0003));
            Check.Equal(24.0, Timecode.NominalRate(24));
            Check.Equal(15.0, Timecode.NominalRate(15.0));
            Check.Equal(26.0, Timecode.NominalRate(26.0));
        }

        [Test("test_core_timecode.BaseTest.test_unusual_rate_uses_rounded_base_without_drop")]
        public static void UnusualRateUsesRoundedBaseWithoutDrop(TestEnv t)
        {
            TimecodeBase tb = Timecode.BaseFor(15);
            Check.Equal(15, tb.Base);
            Check.Equal(false, tb.Drop);
            Check.Equal(":", tb.Separator);
            Check.Equal("0:00:02:01", Timecode.FrameToTimecode(31, tb));
        }

        [Test("test_core_timecode.BaseTest.test_nominal_rate_rejects_bad_values")]
        public static void NominalRateRejectsBadValues(TestEnv t)
        {
            foreach (double bad in new[] { 0, -5, double.NaN, double.PositiveInfinity })
            {
                Check.Throws<ArgumentException>(() => Timecode.NominalRate(bad), "value " + bad);
            }
        }

        [Test("test_core_timecode.ParseTest.test_short_forms_and_separators")]
        public static void ShortFormsAndSeparators(TestEnv t)
        {
            TimecodeBase tb = Timecode.BaseFor(25);
            Check.Equal(307, Timecode.TimecodeToFrame("12:07", tb));
            Check.Equal(1 * 60 * 25 + 2 * 25 + 3, Timecode.TimecodeToFrame("1:02:03", tb));
            Check.Equal(307, Timecode.TimecodeToFrame("  0;00;12;07 ", tb));
            Check.Equal(307, Timecode.TimecodeToFrame("0:00:12:07", tb));
        }

        [Test("test_core_timecode.ParseTest.test_value_errors")]
        public static void ValueErrors(TestEnv t)
        {
            TimecodeBase tb = Timecode.BaseFor(25);
            foreach (string bad in new[] { "0:00:12:25", "", "abc", "1", "0:00:60:00", "0:60:00:00", "1:2:3:4:5", "0:00:-1:00", "0:00:12:0x" })
            {
                Check.Throws<FormatException>(() => Timecode.TimecodeToFrame(bad, tb), "text '" + bad + "'");
            }
            TimecodeBase drop = Timecode.BaseFor(29.97);
            foreach (string dropped in new[] { "0;01;00;00", "0;01;00;01", "0;09;00;01" })
            {
                Check.Throws<FormatException>(() => Timecode.TimecodeToFrame(dropped, drop), "text '" + dropped + "'");
            }
            Check.Equal(17982, Timecode.TimecodeToFrame("0;10;00;00", drop));
            Check.Equal(1, Timecode.TimecodeToFrame("0;00;00;01", drop));
            Check.Throws<ArgumentOutOfRangeException>(() => Timecode.FrameToTimecode(-1, tb));
        }

        [Test("test_core_timecode.ConversionTest.test_seconds_and_frames")]
        public static void SecondsAndFrames(TestEnv t)
        {
            Check.Equal(307, Timecode.SecondsToFrame(12.28, 25));
            Check.Equal(0, Timecode.SecondsToFrame(-1.0, 25));
            Check.Equal(1, Timecode.SecondsToFrame(0.04, 25));
            Check.Near(12.30, Timecode.FrameToSeconds(307, 25), 7);
            double fps = 30000.0 / 1001.0;
            foreach (int frame in new[] { 0, 1, 1799, 100000 })
            {
                Check.Equal(frame, Timecode.SecondsToFrame(Timecode.FrameToSeconds(frame, fps), fps));
            }
            Check.Equal(2310, Timecode.DurationFrames(92.4, 25));
            // Python round() is banker's rounding: 0.5 -> 0, 1.5 -> 2, 2.5 -> 2.
            Check.Equal(0, Timecode.DurationFrames(0.5, 1));
            Check.Equal(2, Timecode.DurationFrames(1.5, 1));
            Check.Equal(2, Timecode.DurationFrames(2.5, 1));
        }

        [Test("test_core_timecode.ConversionTest.test_fps_text")]
        public static void FpsText(TestEnv t)
        {
            Check.Equal("25", Timecode.FormatFps(25));
            Check.Equal("25", Timecode.FormatFps(25.0));
            Check.Equal("29.97", Timecode.FormatFps(29.97));
            Check.Equal("29.97", Timecode.FormatFps(30000.0 / 1001.0));
            Check.Equal("23.976", Timecode.FormatFps(24000.0 / 1001.0));
            Check.Equal("59.94", Timecode.FormatFps(59.94));
            Check.Equal("100", Timecode.FormatFps(100));
            // Decimal ties are decided by the exact binary value: 2.0005 is above the tie, 1.0005 below it.
            Check.Equal("2.001", Timecode.FormatFps(2.0005));
            Check.Equal("1", Timecode.FormatFps(1.0005));
            Check.Equal("0.001", Timecode.FormatFps(0.0005));
        }

        [Test("test_core_timecode.ConversionTest.test_parse_fps")]
        public static void ParseFps(TestEnv t)
        {
            Check.Equal(25.0, Timecode.ParseFps("25"));
            Check.Equal(29.97, Timecode.ParseFps(" 29.97 "));
            Check.Equal(23.976, Timecode.ParseFps("23.976"));
            foreach (string bad in new[] { "", "abc", "0", "-3", "1e3", "nan", "inf", "25 fps", "1_0" })
            {
                Check.Throws<FormatException>(() => Timecode.ParseFps(bad), "text '" + bad + "'");
            }
            foreach (double fps in new[] { 25.0, 29.97, 23.976, 59.94 })
            {
                Check.Equal(fps, Timecode.ParseFps(Timecode.FormatFps(fps)));
            }
        }
    }
}
