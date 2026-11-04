using System;
using System.IO;
using QuickCut;

// Unit tests for QuickCut. Built and run by test.cmd, together with src\*.cs.
//
// The tests only call pure code (parsing, math, drawing geometry). They never read or write
// %APPDATA%\QuickCut\config.ini, send keys, change the volume or start programs.
namespace QuickCutTests
{
    static class Runner
    {
        static int passed, failed, known, nowPassing;
        static string group = "";

        [STAThread]
        static int Main()
        {
            // Safety net: the tests must leave the real settings file alone.
            DateTime configBefore = ConfigStamp();

            Tests.KeyComboTests();
            Tests.RingItemTests();
            Tests.VolumeStepTests();
            Tests.HitTestTests();
            Tests.HelperTests();
            Tests.FileTests();
            SettingsTests.FitScaleTests();
            TransferTests.Run();
            Tests.KnownBugs();

            Group("Safety");
            Check("config.ini was not touched", ConfigStamp() == configBefore);

            Console.WriteLine();
            Console.WriteLine("{0} passed, {1} failed, {2} known bugs{3}", passed, failed, known,
                nowPassing > 0 ? ", " + nowPassing + " known bug(s) now pass: move them to a normal check" : "");
            return failed > 0 ? 1 : 0;
        }

        static DateTime ConfigStamp()
        {
            return File.Exists(Config.FilePath) ? File.GetLastWriteTimeUtc(Config.FilePath) : DateTime.MinValue;
        }

        public static void Group(string name)
        {
            group = name;
            Console.WriteLine();
            Console.WriteLine("== " + name);
        }

        // A normal check: a failure fails the run.
        public static void Check(string name, bool ok, string detail = "")
        {
            if (ok) passed++; else failed++;
            Console.WriteLine("  " + (ok ? "PASS " : "FAIL ") + name + (ok || detail.Length == 0 ? "" : "  -> " + detail));
        }

        // A check that documents a bug that isn't fixed yet. Reported, but it doesn't fail the run.
        // Once the bug is fixed it passes: move it to a normal Check in its own group.
        public static void KnownBug(string name, bool ok, string detail = "")
        {
            if (ok) { nowPassing++; Console.WriteLine("  FIXED " + name + "  (now passes: make it a normal check)"); }
            else { known++; Console.WriteLine("  KNOWN " + name + (detail.Length == 0 ? "" : "  -> " + detail)); }
        }
    }
}
