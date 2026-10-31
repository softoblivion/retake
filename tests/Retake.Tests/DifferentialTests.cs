using System;
using System.Globalization;
using System.IO;
using Retake.Core;

namespace Retake.Tests
{
    /// <summary>Pseudo-random inputs whose expected results were computed by the 1.x core (fixtures/differential/cases.txt).</summary>
    public static class DifferentialTests
    {
        private static double D(string text)
        {
            return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        [Test("differential.timecode_rounding_regions_and_json_floats_match_the_1x_core")]
        public static void MatchesThe1xCore(TestEnv t)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixtures", "differential", "cases.txt");
            string[] lines = File.ReadAllLines(path);
            int tc = 0, nom = 0, reg = 0, rep = 0, sec = 0, changed = 0;
            foreach (string line in lines)
            {
                if (line.Length == 0) continue;
                string[] f = line.Split('\t');
                switch (f[0])
                {
                    case "tc":
                    {
                        TimecodeBase tb = Timecode.BaseFor(D(f[1]));
                        int frame = int.Parse(f[2], CultureInfo.InvariantCulture);
                        Check.Equal(f[3], Timecode.FrameToTimecode(frame, tb), line);
                        Check.Equal(frame, Timecode.TimecodeToFrame(f[3], tb), line);
                        tc++;
                        break;
                    }
                    case "nom":
                    {
                        // Retake 2.3 added 47.952 and 119.88 to the table and keeps 24.975, 49.95 and 99.9 as they are; a
                        // rate the 1.x core snapped differently must give exactly one of those answers.
                        double input = D(f[1]), old = D(f[2]), now = Timecode.NominalRate(input);
                        if (now != old)
                        {
                            bool newEntry = (now == 47.952 || now == 119.88) && Math.Abs(input - now) / now <= 0.005
                                && (old == input || Math.Abs(input - now) < Math.Abs(input - old));
                            bool kept = now == input && (old == 25 || old == 50 || old == 100);
                            Check.True(newEntry || kept, line + ": " + now.ToString("R", CultureInfo.InvariantCulture));
                            changed++;
                        }
                        Check.Equal(f[3], Timecode.FormatFps(input), line);
                        nom++;
                        break;
                    }
                    case "reg":
                    {
                        double v = D(f[1]);
                        Check.Equal(f[2], new Region("point", v, 1.0 - v, v, v).ToText(), line);
                        reg++;
                        break;
                    }
                    case "rep":
                        Check.Equal(f[2], Json.Write(D(f[1])), line);
                        rep++;
                        break;
                    case "sec":
                        Check.Equal(int.Parse(f[3], CultureInfo.InvariantCulture), Timecode.SecondsToFrame(D(f[1]), D(f[2])), line);
                        Check.Equal(int.Parse(f[4], CultureInfo.InvariantCulture), Timecode.DurationFrames(D(f[1]), D(f[2])), line);
                        sec++;
                        break;
                    default:
                        throw new AssertException("unknown case kind " + f[0]);
                }
            }
            Check.True(tc > 700 && nom == 300 && reg == 400 && rep == 300 && sec == 200, tc + " " + nom + " " + reg + " " + rep + " " + sec);
            Check.True(changed > 0 && changed < 40, "rates snapped differently from the 1.x core: " + changed);
        }
    }
}
