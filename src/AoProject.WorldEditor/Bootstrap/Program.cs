using AoProject.WorldEditor.Presentation.Forms;

namespace AoProject.WorldEditor.Bootstrap;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
        var form = new MainForm();
        var bundle = Environment.GetEnvironmentVariable("AO_WORLD_EDITOR_BUNDLE");
        if (!string.IsNullOrWhiteSpace(bundle))
        {
            var rebuild = Environment.GetEnvironmentVariable("AO_WORLD_EDITOR_REBUILD") == "1";
            var auditOnly = Environment.GetEnvironmentVariable("AO_WORLD_EDITOR_AUDIT_ONLY") == "1";
            var looseTeleportOnly = Environment.GetEnvironmentVariable("AO_WORLD_EDITOR_LOOSE_TELEPORT_ONLY") == "1";
            var stitchedExperimental = Environment.GetEnvironmentVariable("AO_WORLD_EDITOR_STITCH_EXPERIMENTAL") == "1";
            form.Shown += async (_, _) =>
            {
                if (looseTeleportOnly)
                    await form.AuditLooseTeleportsAsync(bundle);
                else if (auditOnly)
                    await form.AuditStitchAsync(bundle);
                else if (stitchedExperimental)
                    await form.CompileStitchedExperimentalAsync(bundle);
                else
                {
                    if (rebuild) await form.RebuildBundleAsync(bundle);
                    await form.OpenBundlePreviewDirectory(bundle);
                }
            };
        }
        System.Windows.Forms.Application.Run(form);
    }
}
