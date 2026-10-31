using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Retake.Core;

namespace Retake.Tests
{
    /// <summary>Regions, the notes file round trip, image names, the readable text and the notes folder; the test names follow the 1.x suite they match.</summary>
    public static class NotesTests
    {
        private const string Sample =
            "# Frame Notes 1\n"
            + "# source: promo_v3.mp4\n"
            + "# fps: 25\n"
            + "# duration: 0:01:32:10\n"
            + "0:00:12:07\tFIX\tlogo flickers for two frames\trect 0.540,0.360 0.880,0.800\t0-00-12-07_FIX.png\n"
            + "0:00:40:00\tADD\tsubtitle for the second speaker\n"
            + "0:01:02:15\tREMOVE\tsecond take of the handshake\tpoint 0.512,0.300\t0-01-02-15_REMOVE.png\n";

        // --- regions ------------------------------------------------------------------------------------------------

        [Test("test_core_notes.RegionTest.test_text_round_trip")]
        public static void RegionTextRoundTrip(TestEnv t)
        {
            Region r = Region.FromText("rect 0.540,0.360 0.880,0.800");
            Check.Equal("rect", r.Shape);
            Check.Equal(0.54, r.X0);
            Check.Equal(0.36, r.Y0);
            Check.Equal(0.88, r.X1);
            Check.Equal(0.8, r.Y1);
            Check.Equal("rect 0.540,0.360 0.880,0.800", r.ToText());
            Region p = Region.FromText("point 0.512,0.300");
            Check.Equal(0.512, p.X0);
            Check.Equal(0.3, p.Y0);
            Check.Equal(0.512, p.X1);
            Check.Equal(0.3, p.Y1);
            Check.Equal("point 0.512,0.300", p.ToText());
        }

        [Test("test_core_notes.RegionTest.test_three_decimals_and_normalisation")]
        public static void RegionThreeDecimalsAndNormalisation(TestEnv t)
        {
            var r = new Region("rect", 0.88888, 0.8, 0.54, 0.36);
            Check.Equal("rect 0.540,0.360 0.889,0.800", r.ToText());
            JsonObject d = r.ToJson();
            Check.Equal(new[] { "shape", "x0", "y0", "x1", "y1" }, d.Keys.ToList());
            Check.Equal("rect", d["shape"]);
            Check.Equal(0.54, d["x0"]);
            Check.Equal(0.36, d["y0"]);
            Check.Equal(0.889, d["x1"]);
            Check.Equal(0.8, d["y1"]);
            Check.Equal("rect 0.000,0.000 1.000,1.000", new Region("rect", 0, 0, 1, 1).ToText());
            // Python round() decides ties by the exact binary value: 0.2345 is below the tie, 0.0005 above it.
            Check.Equal("point 0.234,0.001", new Region("point", 0.2345, 0.0005, 0, 0).ToText());
            Check.Equal("point 0.500,0.125", new Region("point", 0.5, 0.125, 0, 0).ToText());
        }

        [Test("test_core_notes.RegionTest.test_dict_round_trip")]
        public static void RegionDictRoundTrip(TestEnv t)
        {
            var d = new JsonObject();
            d["shape"] = "rect"; d["x0"] = 0.54; d["y0"] = 0.36; d["x1"] = 0.88; d["y1"] = 0.8;
            Check.Equal(Json.Write(d), Json.Write(Region.FromJson(d).ToJson()));
            var pd = new JsonObject();
            pd["shape"] = "point"; pd["x0"] = 0.2; pd["y0"] = 0.4;
            Region point = Region.FromJson(pd);
            var pointJson = point.ToJson();
            Check.Equal(0.2, pointJson["x1"]);
            Check.Equal(0.4, pointJson["y1"]);
            var fd = new JsonObject();
            fd["shape"] = "rect"; fd["x0"] = 0.9; fd["y0"] = 0.9; fd["x1"] = 0.1; fd["y1"] = 0.2;
            Region flipped = Region.FromJson(fd);
            Check.Equal(new[] { 0.1, 0.2, 0.9, 0.9 }, new[] { flipped.X0, flipped.Y0, flipped.X1, flipped.Y1 });
            // whole numbers are accepted as fractions
            var wd = new JsonObject();
            wd["shape"] = "point"; wd["x0"] = 1L; wd["y0"] = 0L;
            Check.Equal("point 1.000,0.000", Region.FromJson(wd).ToText());
        }

        [Test("test_core_notes.RegionTest.test_junk_is_rejected")]
        public static void RegionJunkIsRejected(TestEnv t)
        {
            foreach (string bad in new[] { "", "rect", "rect 0.1,0.1", "circle 0.1,0.1", "point 1.5,0.2", "point -0.1,0.2",
                "rect 0.1,0.1 0.2,nan", "point a,b", "rect 0.1,0.1 0.2,0.2 0.3,0.3", "point 0.1;0.2" })
            {
                Check.Throws<ArgumentException>(() => Region.FromText(bad), "text '" + bad + "'");
            }
            var objects = new List<object?>();
            objects.Add(null);
            objects.Add(new JsonObject());
            objects.Add(Obj("shape", "rect"));
            objects.Add(Obj("shape", "blob", "x0", 0L, "y0", 0L));
            objects.Add(Obj("shape", "point", "x0", 2L, "y0", 0L));
            objects.Add(Obj("shape", "point", "x0", "0.1", "y0", 0L));
            objects.Add(Obj("shape", "rect", "x0", 0L, "y0", 0L, "x1", 1L));
            objects.Add("text");
            foreach (object? bad in objects)
            {
                Check.Throws<ArgumentException>(() => Region.FromJson(bad), "value " + (bad == null ? "null" : Json.Write(bad)));
            }
            Check.Throws<ArgumentException>(() => new Region("rect", 2, 0, 1, 1).ToText());
            Check.Throws<ArgumentException>(() => new Region("blob", 0, 0, 1, 1).Normalized());
            Check.Throws<ArgumentException>(() => new Region("rect", double.NaN, 0, 1, 1).ToText());
        }

        private static JsonObject Obj(params object[] pairs)
        {
            var obj = new JsonObject();
            for (int i = 0; i < pairs.Length; i += 2) obj[(string)pairs[i]] = pairs[i + 1];
            return obj;
        }

        // --- image names and paths ----------------------------------------------------------------------------------

        [Test("test_core_notes.ImageNameTest.test_names_and_collisions")]
        public static void ImageNamesAndCollisions(TestEnv t)
        {
            TimecodeBase tb = Timecode.BaseFor(25);
            var note = new Note("a", 307, "FIX", "x");
            Check.Equal("0-00-12-07_FIX.png", NotesLayout.ImageName(note, tb, new string[0]));
            Check.Equal("0-00-12-07_FIX_2.png", NotesLayout.ImageName(note, tb, new[] { "0-00-12-07_FIX.png" }));
            Check.Equal("0-00-12-07_FIX_3.png", NotesLayout.ImageName(note, tb, new[] { "0-00-12-07_FIX.png", "0-00-12-07_FIX_2.png" }));
            Check.Equal("0-00-12-07_FIX_2.png", NotesLayout.ImageName(note, tb, new[] { "0-00-12-07_fix.PNG" }), "case-insensitive");
            TimecodeBase drop = Timecode.BaseFor(29.97);
            Check.Equal("0-01-00-02_ADD.png", NotesLayout.ImageName(new Note("b", 1800, "ADD", ""), drop, new string[0]));
        }

        [Test("test_core_notes.ImageNameTest.test_paths")]
        public static void Paths_(TestEnv t)
        {
            string video = @"D:\clips\promo_v3.mp4";
            Check.Equal(@"D:\clips\promo_v3_notes", NotesLayout.NotesDirFor(video));
            Check.Equal(@"D:\clips\promo_v3_notes\promo_v3_notes.txt", NotesLayout.NotesPathFor(video));
            Check.Equal(@"D:\clips\promo_v3_notes\promo_v3_notes.md", NotesLayout.MarkdownPathFor(video));
            Check.Equal(@"D:\clips\promo_v3_notes\promo_v3_notes.zip", NotesLayout.PackagePathFor(video));
            Check.Equal(@"D:\clips\promo_v3_notes\frames", NotesLayout.ImagesDirFor(video));
            Check.Equal(@"D:\clips\promo_v3_notes.txt", NotesLayout.LegacyNotesPathFor(video));
            Check.Equal(@"D:\clips\promo_v3_notes\promo_v3_notes.txt", NotesLayout.NotesPathFor("D:/clips/promo_v3.mp4"));
            Check.Equal(@"a.b_notes\a.b_notes.txt", NotesLayout.NotesPathFor("a.b.mov"));
            Check.True(NotesLayout.IsPlainName("0-00-12-07_FIX.png"));
            foreach (string? bad in new string?[] { "", null, ".", "..", "..\\x.png", "a/b.png", "C:\\x.png", "C:x.png" })
            {
                Check.False(NotesLayout.IsPlainName(bad), bad ?? "null");
            }
        }

        // --- parse / render -----------------------------------------------------------------------------------------

        [Test("test_core_notes.ParseRenderTest.test_sample_round_trip")]
        public static void SampleRoundTrip(TestEnv t)
        {
            NotesFile nf = NotesFile.Parse(Sample);
            Check.Equal("promo_v3.mp4", nf.Source);
            Check.Equal(25.0, nf.Fps);
            Check.Equal(2310, nf.DurationFrames);
            Check.Equal(new[] { 307, 1000, 1565 }, nf.Notes.Select(n => n.Frame).ToList());
            Check.Equal(new[] { "FIX", "ADD", "REMOVE" }, nf.Notes.Select(n => n.Kind).ToList());
            Check.Equal("rect 0.540,0.360 0.880,0.800", nf.Notes[0].Region!.ToText());
            Check.Equal("0-00-12-07_FIX.png", nf.Notes[0].Image);
            Check.Null(nf.Notes[1].Region);
            Check.Null(nf.Notes[1].Image);
            Check.Equal("point", nf.Notes[2].Region!.Shape);
            Check.Equal(0, nf.Extra.Count);
            Check.Equal(3, nf.Notes.Select(n => n.Id).Distinct().Count());
            foreach (Note n in nf.Notes) Check.True(Check.IsHex8(n.Id), n.Id);
            Check.Equal(Sample, nf.Render());
        }

        [Test("test_core_notes.ParseRenderTest.test_extra_lines_are_kept_after_notes")]
        public static void ExtraLinesAreKeptAfterNotes(TestEnv t)
        {
            string text = Sample.Replace("0:00:40:00", "this line is hand written\n0:00:40:00")
                + "0:99:99:99\tFIX\tbad timecode\n0:00:50:00\tBUG\tbad kind\n# trailing comment\n\n";
            NotesFile nf = NotesFile.Parse(text);
            Check.Equal(3, nf.Notes.Count);
            var expected = new[] { "this line is hand written", "0:99:99:99\tFIX\tbad timecode", "0:00:50:00\tBUG\tbad kind", "# trailing comment" };
            Check.Equal(expected, nf.Extra);
            string output = nf.Render();
            string[] lines = output.Split('\n');
            Check.Equal("", lines[lines.Length - 1]);
            Check.Equal(nf.Extra, lines.Skip(lines.Length - 5).Take(4).ToList());
            Check.Equal(nf.Extra, NotesFile.Parse(output).Extra);
        }

        [Test("test_core_notes.ParseRenderTest.test_bad_region_and_extra_column_go_to_extra")]
        public static void BadRegionAndExtraColumnGoToExtra(TestEnv t)
        {
            string text = "# Frame Notes 1\n# source: v.mp4\n# fps: 25\n"
                + "0:00:01:00\tFIX\ta\trect 2.0,0.1 0.3,0.3\n"
                + "0:00:02:00\tFIX\ta\tpic.png\textra.png\n";
            NotesFile nf = NotesFile.Parse(text);
            Check.Equal(0, nf.Notes.Count);
            Check.Equal(2, nf.Extra.Count);
        }

        [Test("test_core_notes.ParseRenderTest.test_image_without_region_and_empty_text")]
        public static void ImageWithoutRegionAndEmptyText(TestEnv t)
        {
            string text = "# Frame Notes 1\n# source: v.mp4\n# fps: 25\n0:00:01:00\tFIX\t\n0:00:02:00\tADD\tx\tshot.png\n";
            NotesFile nf = NotesFile.Parse(text);
            Check.Equal("", nf.Notes[0].Text);
            Check.Equal("shot.png", nf.Notes[1].Image);
            Check.Null(nf.Notes[1].Region);
            Check.Equal(text, nf.Render());
        }

        [Test("test_core_notes.ParseRenderTest.test_duration_is_optional")]
        public static void DurationIsOptional(TestEnv t)
        {
            string text = "# Frame Notes 1\n# source: v.mp4\n# fps: 29.97\n0;01;00;02\tFIX\tdrop frame\n";
            NotesFile nf = NotesFile.Parse(text);
            Check.Null(nf.DurationFrames);
            Check.Equal(1800, nf.Notes[0].Frame);
            Check.Equal(text, nf.Render());
            Check.NotContains("# duration", nf.Render());
        }

        [Test("test_core_notes.ParseRenderTest.test_crlf_and_bom_are_accepted")]
        public static void CrlfAndBomAreAccepted(TestEnv t)
        {
            NotesFile nf = NotesFile.Parse(((char)0xFEFF) + Sample.Replace("\n", "\r\n"));
            Check.Equal(3, nf.Notes.Count);
            Check.NotContains("\r", nf.Render());
        }

        [Test("test_core_notes.ParseRenderTest.test_render_sorts_by_frame_then_order_and_cleans_text")]
        public static void RenderSortsByFrameThenOrderAndCleansText(TestEnv t)
        {
            var nf = new NotesFile("v.mp4", 25.0, null);
            nf.Notes.Add(new Note("00000001", 100, "ADD", "second\ton\r\nthe frame") { Order = 3 });
            nf.Notes.Add(new Note("00000002", 100, "FIX", "first") { Order = 2 });
            nf.Notes.Add(new Note("00000003", 5, "REMOVE", "early") { Order = 9 });
            string[] lines = nf.Render().Split('\n');
            Check.Equal("0:00:00:05\tREMOVE\tearly", lines[3]);
            Check.Equal("0:00:04:00\tFIX\tfirst", lines[4]);
            Check.Equal("0:00:04:00\tADD\tsecond on  the frame", lines[5]);
            Check.True(nf.Render().EndsWith("\n", StringComparison.Ordinal));
            Check.Equal("a b c", Note.NormalizeText("  a\tb\nc \r"));
            Check.Equal("a b c d", Note.NormalizeText("a" + (char)0x2028 + "b" + (char)0x85 + "c" + (char)0xB + "d"));
        }

        [Test("test_core_notes.ParseRenderTest.test_format_errors")]
        public static void FormatErrors(TestEnv t)
        {
            NotesFormatException ex = Check.Throws<NotesFormatException>(() => NotesFile.Parse("# Frame Notes 2\n# source: v.mp4\n# fps: 25\n"));
            Check.Contains("newer", ex.Message);
            foreach (string bad in new[]
            {
                "",
                "hello\n",
                "# Frame Notes 1\n# fps: 25\n",
                "# Frame Notes 1\n# source: v.mp4\n",
                "# Frame Notes 1\n# source: v.mp4\n# fps: fast\n",
            })
            {
                Check.Throws<NotesFormatException>(() => NotesFile.Parse(bad), "text '" + bad + "'");
            }
            Check.Throws<NotesFormatException>(() => NotesFile.Parse("# Frame Notes 99999999999999999999\n# source: v.mp4\n# fps: 25\n"));
        }

        [Test("test_core_notes.ParseRenderTest.test_ids_are_unique_hex")]
        public static void IdsAreUniqueHex(TestEnv t)
        {
            var taken = new HashSet<string>();
            for (int i = 0; i < 500; i++)
            {
                string value = Note.NewId(taken);
                Check.True(Check.IsHex8(value), value);
                Check.False(taken.Contains(value));
                taken.Add(value);
            }
        }
        // --- general notes ------------------------------------------------------------------------------------------

        private const string Header = "# Frame Notes 1\n# source: promo_v3.mp4\n# fps: 25\n# duration: 0:01:32:10\n";

        [Test("notes.general_line_parses_renders_and_comes_first")]
        public static void GeneralLineParsesRendersAndComesFirst(TestEnv t)
        {
            string text = Header
                + "-\tFIX\tmusic is too loud overall\n"
                + "  -  \tADD\tsecond general note\n"
                + "0:00:12:07\tFIX\tlogo flickers\trect 0.540,0.360 0.880,0.800\t0-00-12-07_FIX.png\n";
            NotesFile nf = NotesFile.Parse(text);
            Check.Equal(3, nf.Notes.Count);
            Check.Equal(0, nf.Extra.Count);
            Check.True(nf.Notes[0].IsGeneral);
            Check.Null(nf.Notes[0].Frame);
            Check.Equal("FIX", nf.Notes[0].Kind);
            Check.Equal("music is too loud overall", nf.Notes[0].Text);
            Check.Null(nf.Notes[0].Region);
            Check.Null(nf.Notes[0].Image);
            Check.True(nf.Notes[1].IsGeneral);
            Check.Equal("second general note", nf.Notes[1].Text);
            Check.False(nf.Notes[2].IsGeneral);
            Check.Equal(307, nf.Notes[2].Frame);
            Check.Equal(new[] { 1, 2, 3 }, nf.Notes.Select(n => n.Order).ToList());
            Check.Equal(Header + "-\tFIX\tmusic is too loud overall\n-\tADD\tsecond general note\n"
                + "0:00:12:07\tFIX\tlogo flickers\trect 0.540,0.360 0.880,0.800\t0-00-12-07_FIX.png\n", nf.Render());
            // render and parse are inverses once the general lines are in front
            Check.Equal(nf.Render(), NotesFile.Parse(nf.Render()).Render());
        }

        [Test("notes.general_line_after_the_notes_of_frames_as_1x_writes_it")]
        public static void GeneralLineAfterTheFrameNotes(TestEnv t)
        {
            // Frame Notes 1.x keeps the general line as an unparsed line and writes it back after its notes.
            string text = Header
                + "0:00:40:00\tADD\tsubtitle\n"
                + "0:00:12:07\tFIX\tlogo\n"
                + "-\tREMOVE\tcut the intro\n";
            NotesFile nf = NotesFile.Parse(text);
            Check.Equal(0, nf.Extra.Count);
            Check.Equal(3, nf.Notes.Count);
            Check.True(nf.Notes[2].IsGeneral);
            Check.Equal(Header + "-\tREMOVE\tcut the intro\n0:00:12:07\tFIX\tlogo\n0:00:40:00\tADD\tsubtitle\n", nf.Render());
        }

        [Test("notes.general_line_with_region_or_image_column_goes_to_extra")]
        public static void GeneralLineWithRegionOrImageGoesToExtra(TestEnv t)
        {
            string withRegion = "-\tFIX\tx\trect 0.100,0.100 0.200,0.200";
            string withImage = "-\tFIX\tx\tshot.png";
            string withBoth = "-\tFIX\tx\trect 0.100,0.100 0.200,0.200\tshot.png";
            string emptyImageColumn = "-\tFIX\tx\t\t\t";
            NotesFile nf = NotesFile.Parse(Header + withRegion + "\n" + withImage + "\n" + withBoth + "\n");
            Check.Equal(0, nf.Notes.Count);
            Check.Equal(new[] { withRegion, withImage, withBoth }, nf.Extra);
            Check.Equal(Header + withRegion + "\n" + withImage + "\n" + withBoth + "\n", nf.Render());
            // six columns never parse; empty 4th and 5th columns are accepted
            Check.Equal(1, NotesFile.Parse(Header + emptyImageColumn + "\n").Extra.Count);
            nf = NotesFile.Parse(Header + "-\tFIX\tx\t\t\n-\tADD\n-\tADD\ty\t\n");
            Check.Equal(3, nf.Notes.Count);
            Check.Equal(0, nf.Extra.Count);
            Check.Equal("", nf.Notes[1].Text);
            Check.Equal(Header + "-\tFIX\tx\n-\tADD\t\n-\tADD\ty\n", nf.Render());
        }

        [Test("notes.general_notes_sort_before_the_notes_of_frames_by_order")]
        public static void GeneralNotesSortBeforeFrameNotes(TestEnv t)
        {
            var nf = new NotesFile("v.mp4", 25.0, null);
            nf.Notes.Add(new Note("00000001", 0, "ADD", "frame zero") { Order = 1 });
            nf.Notes.Add(new Note("00000002", null, "FIX", "late general") { Order = 9 });
            nf.Notes.Add(new Note("00000003", null, "ADD", "early general\ttext") { Order = 4 });
            nf.Notes.Add(new Note("00000004", 5, "FIX", "frame five") { Order = 2, Image = "5.png" });
            string[] lines = nf.Render().Split('\n');
            Check.Equal("-\tADD\tearly general text", lines[3]);
            Check.Equal("-\tFIX\tlate general", lines[4]);
            Check.Equal("0:00:00:00\tADD\tframe zero", lines[5]);
            Check.Equal("0:00:00:05\tFIX\tframe five\t5.png", lines[6]);
            // a general note never writes a region or an image
            var general = new Note("00000005", null, "FIX", "g") { Order = 1, Region = Region.CreatePoint(0.5, 0.5), Image = "x.png" };
            var other = new NotesFile("v.mp4", 25.0, null);
            other.Notes.Add(general);
            Check.Equal("# Frame Notes 1\n# source: v.mp4\n# fps: 25\n-\tFIX\tg\n", other.Render());
        }

        // --- readable text --------------------------------------------------------------------------------------------

        private static readonly string Em = " " + (char)0x2014 + " ";

        [Test("notes.readable_text_of_the_sample_has_no_markdown_and_no_images")]
        public static void ReadableTextOfTheSample(TestEnv t)
        {
            NotesFile nf = NotesFile.Parse(Sample);
            Check.Equal(
                "promo_v3.mp4\n"
                + "\n"
                + "0:00:12:07 FIX" + Em + "logo flickers for two frames\n"
                + "0:00:40:00 ADD" + Em + "subtitle for the second speaker\n"
                + "0:01:02:15 REMOVE" + Em + "second take of the handshake\n",
                nf.RenderReadable());
        }

        [Test("notes.readable_text_with_general_notes")]
        public static void ReadableTextWithGeneralNotes(TestEnv t)
        {
            var nf = new NotesFile("promo_v3.mp4", 25.0, 2310);
            nf.Notes.Add(new Note("a", 1000, "ADD", "subtitle for the second speaker") { Order = 1 });
            nf.Notes.Add(new Note("b", 307, "FIX", "logo flickers for two frames") { Order = 2 });
            nf.Notes.Add(new Note("c", null, "FIX", "music is too loud overall") { Order = 3 });
            Check.Equal(
                "promo_v3.mp4\n"
                + "\n"
                + "General\n"
                + "FIX" + Em + "music is too loud overall\n"
                + "\n"
                + "0:00:12:07 FIX" + Em + "logo flickers for two frames\n"
                + "0:00:40:00 ADD" + Em + "subtitle for the second speaker\n",
                nf.RenderReadable());
            // general notes only: no blank line after the block
            nf.Notes.RemoveAll(n => !n.IsGeneral);
            nf.Notes.Add(new Note("d", null, "ADD", "second\ngeneral") { Order = 4 });
            Check.Equal("promo_v3.mp4\n\nGeneral\nFIX" + Em + "music is too loud overall\nADD" + Em + "second general\n", nf.RenderReadable());
        }

        [Test("notes.readable_text_empty_text_no_notes_drop_frame_and_extra_lines")]
        public static void ReadableTextEmptyNoNotesDropFrameAndExtra(TestEnv t)
        {
            var nf = new NotesFile("clip.mp4", 30000.0 / 1001.0, null);
            Check.Equal("clip.mp4\n\nNo notes.\n", nf.RenderReadable());
            nf.Notes.Add(new Note("a", 1800, "ADD", "") { Image = "my frame.png" });
            nf.Notes.Add(new Note("b", 1, "FIX", "  x\ty ") { Order = 2 });
            nf.Notes.Add(new Note("c", null, "REMOVE", "   ") { Order = 3 });
            nf.Extra.Add("hand written");
            string text = nf.RenderReadable();
            Check.Equal("clip.mp4\n\nGeneral\nREMOVE\n\n0;00;00;01 FIX" + Em + "x y\n0;01;00;02 ADD\n", text);
            Check.NotContains("hand written", text);
            Check.NotContains("![", text);
            Check.NotContains("\r", text);
        }


        // --- the notes folder ---------------------------------------------------------------------------------------

        private const string Data =
            "# Frame Notes 1\n# source: promo.mp4\n# fps: 25\n"
            + "0:00:01:00\tFIX\ta\trect 0.100,0.100 0.200,0.200\t0-00-01-00_FIX.png\n0:00:02:00\tADD\tb\n";

        private static string MakeVideo(TestEnv t)
        {
            string video = t.Combine("promo.mp4");
            File.WriteAllBytes(video, new byte[] { (byte)'v', (byte)'i', (byte)'d', (byte)'e', (byte)'o' });
            return video;
        }

        private static string MakeReview(TestEnv t)
        {
            string folder = t.Combine("promo_notes");
            Fx.WriteText(Path.Combine(folder, "promo_notes.txt"), Data);
            Fx.WriteText(Path.Combine(folder, "promo_notes.md"), "# promo.mp4\n");
            Fx.WriteBytes(Path.Combine(folder, "frames", "0-00-01-00_FIX.png"), new byte[] { (byte)'p', (byte)'n', (byte)'g' });
            return folder;
        }

        [Test("test_core_notes.FolderTest.test_summary")]
        public static void Summary(TestEnv t)
        {
            string video = MakeVideo(t);
            Check.Null(NotesLayout.NotesSummaryFor(video));
            Fx.WriteText(t.Combine("promo_notes.txt"), Data);
            NotesSummary s = NotesLayout.NotesSummaryFor(video)!;
            Check.Equal(2, s.Notes);
            Check.Equal(1, s.Frames);
            File.Delete(t.Combine("promo_notes.txt"));
            MakeReview(t);
            s = NotesLayout.NotesSummaryFor(video)!;
            Check.Equal(2, s.Notes);
            Check.Equal(1, s.Frames);
            Fx.WriteText(t.Combine("promo_notes", "promo_notes.txt"), Data.Replace("promo.mp4", "promo.mov"));
            Check.Null(NotesLayout.NotesSummaryFor(video));
        }

        [Test("test_core_notes.FolderTest.test_delete_review")]
        public static void DeleteReview(TestEnv t)
        {
            string video = MakeVideo(t);
            string folder = MakeReview(t);
            Fx.WriteBytes(Path.Combine(folder, "frames", "x.png.part"), new byte[0]);
            Fx.WriteBytes(Path.Combine(folder, "Thumbs.db"), new byte[0]);
            DeleteResult result = NotesLayout.DeleteNotes(video);
            Check.Equal(2, result.Notes);
            Check.Equal(1, result.Frames);
            Check.Equal(folder, result.Path);
            Check.Equal(new[] { "promo.mp4" }, Fx.Names(t.Tmp).Where(n => n != "home").ToList());
            NotesLayoutException ex = Check.Throws<NotesLayoutException>(() => NotesLayout.DeleteNotes(video));
            Check.Equal("There are no notes for promo.mp4.", ex.Message);
        }

        [Test("notes.summary_counts_general_notes_as_notes")]
        public static void SummaryCountsGeneralNotes(TestEnv t)
        {
            string video = MakeVideo(t);
            Fx.WriteText(t.Combine("promo_notes", "promo_notes.txt"), Data + "-\tFIX\tgeneral one\n-\tADD\tgeneral two\n");
            NotesSummary s = NotesLayout.NotesSummaryFor(video)!;
            Check.Equal(4, s.Notes);
            Check.Equal(1, s.Frames);
        }

        [Test("notes.delete_removes_the_package_and_the_legacy_export_with_the_folder")]
        public static void DeleteRemovesPackageAndExport(TestEnv t)
        {
            string video = MakeVideo(t);
            string folder = MakeReview(t);
            Fx.WriteBytes(Path.Combine(folder, "promo_notes.zip"), new byte[] { (byte)'P', (byte)'K' });
            Fx.WriteBytes(Path.Combine(folder, "promo_notes.zip.part"), new byte[0]);
            DeleteResult result = NotesLayout.DeleteNotes(video);
            Check.Equal(2, result.Notes);
            Check.Equal(1, result.Frames);
            Check.False(Directory.Exists(folder));
            Check.Equal(new[] { "promo.mp4" }, Fx.Names(t.Tmp).Where(n => n != "home").ToList());
            // a package beside a file the app does not write is still refused as a whole
            Fx.WriteBytes(Path.Combine(folder, "promo_notes.zip"), new byte[] { 1 });
            Fx.WriteText(Path.Combine(folder, "promo_notes.txt"), Data);
            Fx.WriteBytes(Path.Combine(folder, "other.zip"), new byte[] { 1 });
            NotesLayoutException ex = Check.Throws<NotesLayoutException>(() => NotesLayout.DeleteNotes(video));
            Check.Equal("The notes folder promo_notes holds other files. Delete it by hand.", ex.Message);
            Check.True(File.Exists(Path.Combine(folder, "promo_notes.zip")));
        }

        [Test("notes.image_name_of_a_general_note_is_refused")]
        public static void ImageNameOfAGeneralNoteIsRefused(TestEnv t)
        {
            Check.Throws<ArgumentException>(() => NotesLayout.ImageName(new Note("a", null, "FIX", "x"), Timecode.BaseFor(25), new string[0]));
        }

        [Test("test_core_notes.FolderTest.test_delete_legacy_layout")]
        public static void DeleteLegacyLayout(TestEnv t)
        {
            string video = MakeVideo(t);
            Fx.WriteText(t.Combine("promo_notes.txt"), Data);
            Fx.WriteBytes(t.Combine("promo_notes", "0-00-01-00_FIX.png"), new byte[] { (byte)'p', (byte)'n', (byte)'g' });
            Fx.WriteText(t.Combine("other_notes.txt"), "keep");
            Check.Equal(1, NotesLayout.DeleteNotes(video).Frames);
            Check.Equal(new[] { "other_notes.txt", "promo.mp4" }, Fx.Names(t.Tmp).Where(n => n != "home").ToList());
        }

        [Test("test_core_notes.FolderTest.test_delete_refuses_foreign_content_and_other_videos")]
        public static void DeleteRefusesForeignContentAndOtherVideos(TestEnv t)
        {
            string video = MakeVideo(t);
            string folder = MakeReview(t);
            Fx.WriteText(Path.Combine(folder, "my edit.prproj"), "keep");
            NotesLayoutException ex = Check.Throws<NotesLayoutException>(() => NotesLayout.DeleteNotes(video));
            Check.Equal("The notes folder promo_notes holds other files. Delete it by hand.", ex.Message);
            File.Delete(Path.Combine(folder, "my edit.prproj"));
            Directory.CreateDirectory(Path.Combine(folder, "frames", "sub"));
            Check.Throws<NotesLayoutException>(() => NotesLayout.DeleteNotes(video));
            Directory.Delete(Path.Combine(folder, "frames", "sub"));
            Fx.WriteText(Path.Combine(folder, "promo_notes.txt"), Data.Replace("promo.mp4", "promo.mov"));
            ex = Check.Throws<NotesLayoutException>(() => NotesLayout.DeleteNotes(video));
            Check.Equal("The notes folder promo_notes belongs to promo.mov.", ex.Message);
            // nothing was removed by any refusal
            Check.Equal(new[] { "frames", "promo_notes.md", "promo_notes.txt" }, Fx.Names(folder));
            Check.True(File.Exists(Path.Combine(folder, "frames", "0-00-01-00_FIX.png")));
        }

        [Test("test_core_notes.FolderTest.test_delete_refuses_a_file_in_place_of_the_folder")]
        public static void DeleteRefusesAFileInPlaceOfTheFolder(TestEnv t)
        {
            string video = MakeVideo(t);
            Fx.WriteText(t.Combine("promo_notes"), "not a folder");
            Check.Throws<NotesLayoutException>(() => NotesLayout.DeleteNotes(video));
            Check.True(File.Exists(t.Combine("promo_notes")));
        }

        [Test("notes.delete_and_migrate_refuse_a_junction_in_place_of_the_folder")]
        public static void JunctionInPlaceOfTheFolderIsRefused(TestEnv t)
        {
            string video = MakeVideo(t);
            string real = t.Combine("elsewhere");
            Fx.WriteText(Path.Combine(real, "promo_notes.txt"), Data);
            Fx.WriteBytes(Path.Combine(real, "a.png"), new byte[] { 1 });
            var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c mklink /J \"" + t.Combine("promo_notes") + "\" \"" + real + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            using (var process = System.Diagnostics.Process.Start(psi)!)
            {
                process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                Check.Equal(0, process.ExitCode, "mklink /J");
            }
            NotesLayoutException ex = Check.Throws<NotesLayoutException>(() => NotesLayout.DeleteNotes(video));
            Check.Equal("The notes folder promo_notes holds other files. Delete it by hand.", ex.Message);
            Check.False(NotesLayout.MigrateLayout(video), "images behind a junction are not moved");
            Check.Equal(new[] { "a.png", "promo_notes.txt" }, Fx.Names(real));
            Directory.Delete(t.Combine("promo_notes")); // removes the junction only
            Check.True(File.Exists(Path.Combine(real, "a.png")));
        }

        [Test("notes.migrate_layout_moves_images_then_data_file")]
        public static void MigrateLayout(TestEnv t)
        {
            string video = MakeVideo(t);
            Check.False(NotesLayout.MigrateLayout(video), "nothing to move");
            Fx.WriteText(t.Combine("promo_notes.txt"), Data);
            Fx.WriteBytes(t.Combine("promo_notes", "0-00-01-00_FIX.png"), new byte[] { 1, 2, 3 });
            Fx.WriteBytes(t.Combine("promo_notes", "readme.txt"), new byte[] { 4 });
            var log = new List<string>();
            Log.Listener = (level, message) => log.Add(level + " " + message);
            Check.True(NotesLayout.MigrateLayout(video));
            Check.True(log.Any(l => l.Contains("moved the notes of promo.mp4")));
            Check.Equal(new[] { "frames", "promo_notes.txt", "readme.txt" }, Fx.Names(t.Combine("promo_notes")));
            Check.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(t.Combine("promo_notes", "frames", "0-00-01-00_FIX.png")));
            Check.False(File.Exists(t.Combine("promo_notes.txt")));
            Check.False(NotesLayout.MigrateLayout(video), "second call moves nothing");
        }
    }
}
