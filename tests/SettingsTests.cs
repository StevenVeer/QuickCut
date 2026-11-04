using System.Drawing;
using QuickCut;

namespace QuickCutTests
{
    static class SettingsTests
    {
        static void Check(string name, bool ok, string detail = "") { Runner.Check(name, ok, detail); }

        // The settings window keeps the monitor's scale when it fits, and shrinks just enough when it doesn't.
        public static void FitScaleTests()
        {
            Runner.Group("Settings window size");
            // 1080p with a 48 px taskbar at 100% / 125%: fits, scale unchanged
            Check("1080p at 100% keeps 100%", SettingsForm.FitScale(1f, new Rectangle(0, 0, 1920, 1032)) == 1f);
            Check("1080p at 125% keeps 125%", SettingsForm.FitScale(1.25f, new Rectangle(0, 0, 1920, 1020)) == 1.25f);
            Check("4K at 200% keeps 200%", SettingsForm.FitScale(2f, new Rectangle(0, 0, 3840, 2064)) == 2f);
            // 1080p at 175% (taskbar ~84 px): would run off the bottom, so it shrinks to fit
            float s = SettingsForm.FitScale(1.75f, new Rectangle(0, 0, 1920, 996));
            Check("1080p at 175% shrinks to fit", s < 1.75f && 666 * s + 32 * 1.75f <= 996, "scale " + s);
            // 1366x768 at 125%: also too tall
            s = SettingsForm.FitScale(1.25f, new Rectangle(0, 0, 1366, 708));
            Check("1366x768 at 125% shrinks to fit", s < 1.25f && 666 * s + 32 * 1.25f <= 708, "scale " + s);
            Check("never below 50%", SettingsForm.FitScale(1f, new Rectangle(0, 0, 200, 200)) == 0.5f);
        }
    }
}
