using System;
using System.Collections.Generic;
using Retake.Core;

namespace Retake.Tests
{
    /// <summary>The Markdown notes file as <see cref="NotesFile.RenderMarkdown"/> writes it: layout, image links and the escaping of user text.</summary>
    public static class MarkdownTests
    {
        private static readonly string Em = " " + (char)0x2014 + " ";
        private static readonly List<string> NoImages = new List<string>();

        private static NotesFile File(double fps = 25, int? duration = null, string source = "promo.mp4")
        {
            return new NotesFile(source, fps, duration);
        }

        private static Note FrameNote(int frame, string kind, string text, string? image = null, int order = 1)
        {
            return new Note("n" + order, frame, kind, text) { Image = image, Order = order };
        }

        /// <summary>The paragraph of one frame note whose text is <paramref name="text"/>.</summary>
        private static string Body(string text)
        {
            NotesFile nf = File();
            nf.Notes.Add(FrameNote(25, "FIX", text));
            string md = nf.RenderMarkdown(null, null, null, NoImages);
            string head = "## `0:00:01:00` FIX\n\n";
            int at = md.IndexOf(head, StringComparison.Ordinal);
            Check.True(at >= 0, md);
            return md.Substring(at + head.Length).TrimEnd('\n');
        }

        [Test("markdown.no_notes")]
        public static void NoNotes(TestEnv t)
        {
            Check.Equal("# promo.mp4\n\n25 fps · 0:01:32:10 · 1920 × 1080\n\nNo notes.\n", File(25, 2310).RenderMarkdown(1920, 1080, "", NoImages));
            Check.Equal("# promo.mp4\n\n25 fps\n\nNo notes.\n", File().RenderMarkdown(null, null, null, NoImages), "unknown duration and size are left out");
        }

        [Test("markdown.general_notes_and_notes_on_frames_with_and_without_images")]
        public static void Layout(TestEnv t)
        {
            NotesFile nf = File(30000.0 / 1001, null, "cut v2.mov");
            nf.Notes.Add(new Note("g", null, "DISLIKE", "too long overall") { Order = 1 });
            nf.Notes.Add(FrameNote(1800, "ADD", "title card", "0-01-00-02_ADD.png", 2));
            nf.Notes.Add(FrameNote(18, "REMOVE", "", "gone.png", 3));
            nf.Notes.Add(FrameNote(30, "LIKE", "keep", "my shot (1).png", 4));
            nf.Notes.Add(new Note("e", null, "FIX", "") { Order = 5 });
            nf.Extra.Add("a line that did not parse");
            string md = nf.RenderMarkdown(1280, 720, "Ann", new List<string> { "0-01-00-02_ADD.png", "MY SHOT (1).PNG" });
            // the counts follow the order of the types
            string expected = "# cut v2.mov\n\n29.97 fps · 1280 × 720 · 5 notes: FIX 1, ADD 1, REMOVE 1, LIKE 1, DISLIKE 1\n\nReviewer: Ann\n\n"
                + "## General\n\n- **DISLIKE**" + Em + "too long overall\n- **FIX**\n\n"
                + "## `0;00;00;18` REMOVE\n\n"
                + "## `0;00;01;00` LIKE\n\nkeep\n\n![0;00;01;00 LIKE](frames/my%20shot%20%281%29.png)\n\n"
                + "## `0;01;00;02` ADD\n\ntitle card\n\n![0;01;00;02 ADD](frames/0-01-00-02_ADD.png)\n";
            Check.Equal(expected, md);
            Check.Equal(md, nf.RenderMarkdown(1280, 720, "Ann", new List<string> { "0-01-00-02_ADD.png", "MY SHOT (1).PNG" }), "the same inputs give the same file");
        }

        [Test("markdown.image_names_that_are_not_plain_are_not_linked")]
        public static void NotPlainImages(TestEnv t)
        {
            NotesFile nf = File();
            nf.Notes.Add(FrameNote(1, "FIX", "a", "..\\x.png", 1));
            nf.Notes.Add(FrameNote(2, "FIX", "b", "C:x.png", 2));
            string md = nf.RenderMarkdown(null, null, null, new List<string> { "..\\x.png", "C:x.png" });
            Check.NotContains("](frames/", md);
        }

        [Test("markdown.user_text_is_escaped")]
        public static void Escaping(TestEnv t)
        {
            Check.Equal("plain words, digits 1.5 and promo_v3_final", Body("plain words, digits 1.5 and promo_v3_final"));
            Check.Equal("\\*b\\* \\_x\\_ a_b \\[l\\](u) \\<b\\> \\# x a\\|b \\~\\~s\\~\\~ \\$x\\$", Body("*b* _x_ a_b [l](u) <b> # x a|b ~~s~~ $x$"));
            Check.Equal("\\==h\\== \\%%c\\%% a=b 50%", Body("==h== %%c%% a=b 50%"));
            Check.Equal("back\\\\slash and \\`code\\` &amp;amp; &amp;", Body("back\\slash and `code` &amp; &"));
            Check.Equal("1\\. first", Body("1. first"));
            Check.Equal("12\\) twelfth", Body("12) twelfth"));
            Check.Equal("\\- dash", Body("- dash"));
            Check.Equal("\\+ plus", Body("+ plus"));
            Check.Equal("\\---", Body("---"));
            Check.Equal("\\> quote", Body("> quote"));
            Check.Equal("see <https://example.com/a_b?q=1&r=*2*> and \\*this\\*", Body("see https://example.com/a_b?q=1&r=*2* and *this*"));
            string cyrillic = "Логотип мигает " + char.ConvertFromUtf32(0x1F642);
            Check.Equal(cyrillic, Body(cyrillic));
            // emoji shortcodes stay text (a word joiner after the first colon); timecodes and plain colons stay as typed
            string wj = char.ConvertFromUtf32(0x2060);
            Check.Equal("fix :" + wj + "tada: at end, :" + wj + "+1: and :" + wj + "-1:", Body("fix :tada: at end, :+1: and :-1:"));
            Check.Equal("at 0:00:12:05 and 12:30, a: b", Body("at 0:00:12:05 and 12:30, a: b"));
            // an address leaves the sentence marks after it and a closing parenthesis it did not open
            Check.Equal("see <https://x.com>.", Body("see https://x.com."));
            Check.Equal("(see <https://x.com/a_(b)>), ok; <https://x.com/q?a=1>!", Body("(see https://x.com/a_(b)), ok; https://x.com/q?a=1!"));
        }

        [Test("markdown.the_title_and_the_reviewer_are_escaped")]
        public static void TitleAndReviewer(TestEnv t)
        {
            string md = File(25, null, "# [final]*.mp4").RenderMarkdown(null, null, "Ann *A*", NoImages);
            Check.Equal("# \\# \\[final\\]\\*.mp4\n\n25 fps\n\nReviewer: Ann \\*A\\*\n\nNo notes.\n", md);
        }
    }
}
