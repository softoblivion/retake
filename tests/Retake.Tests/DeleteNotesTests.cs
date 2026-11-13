using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Retake.Core;

namespace Retake.Tests
{
    /// <summary>
    /// A Recycle Bin for the tests: items move into <c>home\bin</c>; an item can be marked as one the bin cannot take
    /// (<see cref="Refuse"/>) or as one whose move fails and leaves it in place (<see cref="Fail"/>).
    /// </summary>
    public sealed class TestBin : IRecycler
    {
        private readonly string _bin;
        private readonly HashSet<string> _refused = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _failing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private int _count;

        public TestBin(TestEnv t)
        {
            _bin = t.Combine("home", "bin");
        }

        /// <summary>The items moved into the bin, as they were named.</summary>
        public readonly List<string> Moved = new List<string>();

        /// <summary>The number of moves asked for.</summary>
        public int Calls;

        public TestBin Refuse(string path)
        {
            _refused.Add(Path.GetFullPath(path));
            return this;
        }

        public TestBin Fail(string path)
        {
            _failing.Add(Path.GetFullPath(path));
            return this;
        }

        public IList<string> NotRecyclable(IList<string> paths)
        {
            return paths.Where(p => _refused.Contains(Path.GetFullPath(p))).ToList();
        }

        public IList<string> Recycle(IList<string> paths)
        {
            Calls++;
            Directory.CreateDirectory(_bin);
            var gone = new List<string>();
            foreach (string path in paths)
            {
                if (_failing.Contains(Path.GetFullPath(path))) continue;
                string to;
                do to = Path.Combine(_bin, (++_count) + "_" + Path.GetFileName(path));
                while (File.Exists(to) || Directory.Exists(to));
                if (Directory.Exists(path)) Directory.Move(path, to);
                else File.Move(path, to);
                Moved.Add(path);
                gone.Add(path);
            }
            return gone;
        }

        /// <summary>Every file in the bin, relative to it.</summary>
        public List<string> Contents()
        {
            return Directory.Exists(_bin) ? Fx.Tree(_bin).Keys.ToList() : new List<string>();
        }
    }

    public static class DeleteNotesTests
    {
        private const string Data = "# Frame Notes 1\n# source: promo.mp4\n# fps: 25\n"
            + "0:00:01:00\tFIX\ta\trect 0.100,0.100 0.200,0.200\t0-00-01-00_FIX.png\n0:00:02:00\tADD\tb\n";

        private static string MakeVideo(TestEnv t)
        {
            string video = t.Combine("promo.mp4");
            File.WriteAllBytes(video, new byte[] { 1, 2, 3 });
            return video;
        }

        private static string MakeReview(TestEnv t)
        {
            string folder = t.Combine("promo_notes");
            Fx.WriteText(Path.Combine(folder, "promo_notes.txt"), Data);
            Fx.WriteText(Path.Combine(folder, "promo_notes.md"), "# promo.mp4\n");
            Fx.WriteBytes(Path.Combine(folder, "frames", "0-00-01-00_FIX.png"), new byte[] { 1 });
            return folder;
        }

        private static List<string> LeftBesideTheVideo(TestEnv t)
        {
            return Fx.Tree(t.Tmp).Keys.Where(k => !k.StartsWith("home/", StringComparison.Ordinal) && k != "promo.mp4").ToList();
        }

        private static DeleteResult? Run(string video, IRecycler bin, bool answer, List<DeletePlan> asked)
        {
            Task<DeleteResult?> task = NotesLayout.DeleteNotesAsking(video, bin, plan =>
            {
                asked.Add(plan);
                return Task.FromResult(answer);
            });
            return task.GetAwaiter().GetResult();
        }

        [Test("notes.delete.recyclable_notes_go_to_the_recycle_bin_without_a_question")]
        public static void Recyclable(TestEnv t)
        {
            string video = MakeVideo(t);
            string folder = MakeReview(t);
            var bin = new TestBin(t);
            var asked = new List<DeletePlan>();
            DeleteResult r = Run(video, bin, true, asked)!;
            Check.Equal(0, asked.Count, "no question when the bin takes everything");
            Check.Equal(new[] { folder }, bin.Moved, "the folder goes as one item");
            Check.Equal(new[] { "1_promo_notes/frames/0-00-01-00_FIX.png", "1_promo_notes/promo_notes.md", "1_promo_notes/promo_notes.txt" }, bin.Contents());
            Check.False(r.Permanently);
            Check.Equal(2, r.Notes);
            Check.Equal(1, r.Frames);
            Check.Equal(0, r.Failed);
            Check.Equal(new string[0], LeftBesideTheVideo(t));
        }

        [Test("notes.delete.not_recyclable_and_cancelled_deletes_nothing")]
        public static void NotRecyclableCancel(TestEnv t)
        {
            string video = MakeVideo(t);
            string folder = MakeReview(t);
            SortedDictionary<string, byte[]> before = Fx.Tree(t.Tmp);
            var bin = new TestBin(t).Refuse(folder);
            var asked = new List<DeletePlan>();
            Check.Null(Run(video, bin, false, asked), "Cancel: no result");
            Check.Equal(1, asked.Count, "one question");
            Check.Equal(2, asked[0].Notes);
            Check.Equal(1, asked[0].Frames);
            Check.Equal(folder, asked[0].Folder, "the question names the place");
            Check.Equal(0, bin.Calls, "nothing was sent to the bin");
            Check.Equal(before.Keys.ToList(), Fx.Tree(t.Tmp).Keys.ToList(), "every file is still there");
            // the delete without the answer refuses too, before anything moves
            NotesLayoutException ex = Check.Throws<NotesLayoutException>(() => NotesLayout.DeleteNotes(video, bin));
            Check.Equal("The Recycle Bin cannot take the notes in promo_notes, so nothing was deleted.", ex.Message);
            Check.Equal(0, bin.Calls);
            Check.Equal(before.Keys.ToList(), Fx.Tree(t.Tmp).Keys.ToList());
            // one item the bin cannot take among several is enough to ask: a mixed folder goes item by item
            Fx.WriteText(Path.Combine(folder, "brief.pdf"), "keep");
            bin = new TestBin(t).Refuse(Path.Combine(folder, "promo_notes.md"));
            asked.Clear();
            Check.Null(Run(video, bin, false, asked));
            Check.Equal(1, asked.Count);
            Check.Equal(0, bin.Calls);
            Check.True(File.Exists(Path.Combine(folder, "frames", "0-00-01-00_FIX.png")), "the images stay when the question is cancelled");
        }

        [Test("notes.delete.not_recyclable_and_answered_deletes_permanently")]
        public static void NotRecyclablePermanent(TestEnv t)
        {
            string video = MakeVideo(t);
            string folder = MakeReview(t);
            Fx.WriteText(Path.Combine(folder, "brief.pdf"), "keep");
            var bin = new TestBin(t).Refuse(Path.Combine(folder, "promo_notes.txt"));
            var asked = new List<DeletePlan>();
            DeleteResult r = Run(video, bin, true, asked)!;
            Check.Equal(1, asked.Count);
            Check.Equal(1, asked[0].Kept);
            Check.True(r.Permanently);
            Check.Equal(2, r.Notes);
            Check.Equal(1, r.Frames);
            Check.Equal(1, r.Kept);
            Check.Equal(0, r.Failed);
            Check.Equal(0, bin.Calls, "a permanent delete never goes through the bin");
            Check.Equal(new string[0], bin.Contents());
            Check.Equal(new[] { "promo_notes/brief.pdf" }, LeftBesideTheVideo(t), "only the other file is left");
            // a whole folder deleted for good, after the answer
            folder = MakeReview(t);
            File.Delete(Path.Combine(folder, "brief.pdf"));
            bin = new TestBin(t).Refuse(folder);
            r = Run(video, bin, true, asked)!;
            Check.True(r.Permanently);
            Check.Equal(2, r.Notes);
            Check.False(Directory.Exists(folder));
            Check.Equal(new string[0], LeftBesideTheVideo(t));
        }

        [Test("notes.delete.a_partial_recycle_counts_only_what_went")]
        public static void PartialRecycle(TestEnv t)
        {
            string video = MakeVideo(t);
            string folder = MakeReview(t);
            Fx.WriteText(Path.Combine(folder, "brief.pdf"), "keep");
            // the data file stays: the images and the Markdown file went, the notes did not
            var bin = new TestBin(t).Fail(Path.Combine(folder, "promo_notes.txt"));
            DeleteResult r = NotesLayout.DeleteNotes(video, bin);
            Check.Equal(0, r.Notes, "the notes are still in their file");
            Check.Equal(1, r.Frames);
            Check.Equal(1, r.Failed);
            Check.False(r.Permanently);
            Check.Equal(new[] { Path.Combine(folder, "frames"), Path.Combine(folder, "promo_notes.md") }, bin.Moved);
            Check.Equal(new[] { "promo_notes/brief.pdf", "promo_notes/promo_notes.txt" }, LeftBesideTheVideo(t));
            // the frames folder stays: the notes went, the images did not
            File.Delete(Path.Combine(folder, "promo_notes.txt"));
            folder = MakeReview(t);
            bin = new TestBin(t).Fail(Path.Combine(folder, "frames"));
            r = NotesLayout.DeleteNotes(video, bin);
            Check.Equal(2, r.Notes);
            Check.Equal(0, r.Frames);
            Check.Equal(1, r.Failed);
            // nothing went: the delete is reported as failed, never as done
            Check.True(File.Exists(Path.Combine(folder, "frames", "0-00-01-00_FIX.png")));
            Fx.WriteText(Path.Combine(folder, "promo_notes.txt"), Data);
            bin = new TestBin(t).Fail(Path.Combine(folder, "frames")).Fail(Path.Combine(folder, "promo_notes.txt"));
            NotesLayoutException ex = Check.Throws<NotesLayoutException>(() => NotesLayout.DeleteNotes(video, bin));
            Check.Equal("The notes folder promo_notes could not be deleted.", ex.Message);
        }

        [Test("notes.delete.a_permanent_delete_stops_at_the_first_failure_and_keeps_the_data_file")]
        public static void PermanentStopsAtFailure(TestEnv t)
        {
            string video = MakeVideo(t);
            string folder = MakeReview(t);
            Fx.WriteText(Path.Combine(folder, "brief.pdf"), "keep");
            string md = Path.Combine(folder, "promo_notes.md");
            using (new FileStream(md, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                DeleteResult r = NotesLayout.DeleteNotes(video, new TestBin(t), true);
                Check.True(r.Permanently);
                Check.Equal(1, r.Frames, "the images went first");
                Check.Equal(0, r.Notes, "the data file is kept after the failure");
                Check.Equal(2, r.Failed, "the Markdown file and the data file are left");
            }
            Check.True(File.Exists(Path.Combine(folder, "promo_notes.txt")));
        }

        [Test("notes.delete.plan_lists_the_items_in_delete_order")]
        public static void PlanTargets(TestEnv t)
        {
            string video = MakeVideo(t);
            string folder = MakeReview(t);
            string frames = Path.Combine(folder, "frames");
            // the app's files only: the folder as one item, the data file removed last within it
            DeletePlan plan = NotesLayout.PlanDelete(video);
            Check.Equal(new[] { folder }, plan.Targets.Select(x => x.Path).ToList());
            Check.True(plan.Targets[0].IsFolder);
            Check.Equal(new[] { Path.Combine(frames, "0-00-01-00_FIX.png"), Path.Combine(folder, "promo_notes.md"), Path.Combine(folder, "promo_notes.txt") }, plan.Targets[0].Files);
            Check.Equal(new[] { frames, folder }, plan.Targets[0].Folders);
            // a mixed folder: the frames folder as one item, then the files, the data file last
            Fx.WriteText(Path.Combine(folder, "brief.pdf"), "keep");
            plan = NotesLayout.PlanDelete(video);
            Check.Equal(new[] { frames, Path.Combine(folder, "promo_notes.md"), Path.Combine(folder, "promo_notes.txt") }, plan.Targets.Select(x => x.Path).ToList());
            Check.Equal(1, plan.Kept);
            // a file of another program among the frames: each image goes on its own
            Fx.WriteText(Path.Combine(frames, "grade_ref.png"), "keep");
            plan = NotesLayout.PlanDelete(video);
            Check.Equal(new[] { Path.Combine(frames, "0-00-01-00_FIX.png"), Path.Combine(folder, "promo_notes.md"), Path.Combine(folder, "promo_notes.txt") }, plan.Targets.Select(x => x.Path).ToList());
            Check.Equal(2, plan.Kept);
            // the 1.0 layout: the data file beside the video carries the notes
            string legacyVideo = t.Combine("old", "promo.mp4");
            Fx.WriteText(t.Combine("old", "promo_notes.txt"), Data);
            File.WriteAllBytes(legacyVideo, new byte[] { 1 });
            plan = NotesLayout.PlanDelete(legacyVideo);
            Check.Equal(new[] { t.Combine("old", "promo_notes.txt") }, plan.Targets.Select(x => x.Path).ToList());
            Check.Equal(2, plan.Notes);
            DeleteResult r = NotesLayout.DeleteNotes(plan, new TestBin(t), false);
            Check.Equal(2, r.Notes);
        }

        [Test("notes.delete.the_shell_bin_never_counts_a_share_or_a_missing_drive_as_recyclable")]
        public static void ShellRecyclerRefusesDrivesWithoutABin(TestEnv t)
        {
            Check.False(ShellRecycler.CanRecycle(@"\\localhost\C$\clips\promo_notes"), "a network path");
            Check.False(ShellRecycler.CanRecycle(@"\\server\share\promo_notes"), "a network path");
            var used = new HashSet<char>(System.IO.DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])));
            char free = "QRSTUVWXYZ".First(c => !used.Contains(c));
            Check.False(ShellRecycler.CanRecycle(free + @":\clips\promo_notes"), "a drive letter with no volume");
            Check.False(ShellRecycler.CanRecycle("C:\\a|b"), "a path that is not valid");
            Check.Equal(new[] { @"\\server\share\x" }, ShellRecycler.Instance.NotRecyclable(new[] { @"\\server\share\x" }));
        }
    }
}
