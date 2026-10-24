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
            int tc = 0, nom = 0, reg = 0, rep = 0, sec = 0;
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
                        Check.Equal(D(f[2]), Timecode.NominalRate(D(f[1])), line);
                        Check.Equal(f[3], Timecode.FormatFps(D(f[1])), line);
                        nom++;
                        break;
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
        }
    }
}
