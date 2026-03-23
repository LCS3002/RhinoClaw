using System;
using System.Drawing.Imaging;
using System.IO;
using Rhino;
using Rhino.Commands;

namespace PenguinClaw
{
    public class PenguinClawSetupToolbarCommand : Command
    {
        public override string EnglishName => "PenguinClawSetupToolbar";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var uiFolder = Path.Combine(appData, "McNeel", "Rhinoceros", "8.0", "UI", "PenguinClaw");
                Directory.CreateDirectory(uiFolder);

                var icon24Path = Path.Combine(uiFolder, "PenguinClaw_24.bmp");
                var icon32Path = Path.Combine(uiFolder, "PenguinClaw_32.bmp");

                using (var bmp24 = PenguinClawIconFactory.CreateBitmap(24))
                {
                    bmp24.Save(icon24Path, ImageFormat.Bmp);
                }

                using (var bmp32 = PenguinClawIconFactory.CreateBitmap(32))
                {
                    bmp32.Save(icon32Path, ImageFormat.Bmp);
                }

                RhinoApp.WriteLine("PenguinClaw toolbar setup helper complete.");
                RhinoApp.WriteLine($"Icon exported: {icon32Path}");
                RhinoApp.WriteLine("Next steps:");
                RhinoApp.WriteLine("1) Toolbar command opens automatically.");
                RhinoApp.WriteLine("2) Create a new toolbar/button.");
                RhinoApp.WriteLine("3) Left click command macro: ! _PenguinClaw");
                RhinoApp.WriteLine($"4) Button appearance icon file: {icon32Path}");

                RhinoApp.RunScript("_Toolbar", false);
                return Result.Success;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"PenguinClawSetupToolbar failed: {ex.Message}");
                return Result.Failure;
            }
        }
    }
}
