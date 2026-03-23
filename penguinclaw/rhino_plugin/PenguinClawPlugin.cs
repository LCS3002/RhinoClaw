using System;
using System.Drawing;
using Rhino.PlugIns;
using Rhino.UI;

// Assembly-level GUID for the plugin - this is what Rhino uses to identify the plugin
[assembly: System.Runtime.InteropServices.Guid("b2c3d4e5-f6a7-8901-bcde-f23456789012")]

// Required plugin description attributes (only the ones not auto-generated)
[assembly: PlugInDescription(DescriptionType.Address, "")]
[assembly: PlugInDescription(DescriptionType.Country, "")]
[assembly: PlugInDescription(DescriptionType.Email, "")]
[assembly: PlugInDescription(DescriptionType.Phone, "")]
[assembly: PlugInDescription(DescriptionType.Fax, "")]
[assembly: PlugInDescription(DescriptionType.Organization, "")]
[assembly: PlugInDescription(DescriptionType.UpdateUrl, "")]
[assembly: PlugInDescription(DescriptionType.WebSite, "")]

namespace PenguinClaw
{
    // Plugin class - Rhino will use the assembly-level GUID to identify it
    public class PenguinClawPlugin : Rhino.PlugIns.PlugIn
    {
        public PenguinClawPlugin()
        {
            Instance = this;
        }

        public static PenguinClawPlugin Instance { get; private set; }

        protected override LoadReturnCode OnLoad(ref string errorMessage)
        {
            try
            {
                // Register the panel
                Rhino.UI.Panels.RegisterPanel(
                    this,
                    typeof(PenguinClawPanel),
                    "PenguinClaw",
                    PenguinClawPanel.PanelIcon
                );

                // Build command registry on background thread (non-blocking)
                System.Threading.ThreadPool.QueueUserWorkItem(_ => RhinoCommandRegistry.Build());

// For toolbar button support - this creates a system icon for the command
                try 
                {
                    var icon = PenguinClawCommand.CreatePenguinClawIcon(32);
                    if (icon != null)
                    {
                        // Create a temporary file path for the icon
                        string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "penguinclaw_icon.bmp");
                        icon.Save(tempPath, System.Drawing.Imaging.ImageFormat.Bmp);
                        
                        // Note: The icon will be available for use by Rhino's toolbar system
                        // Users can manually add it to toolbars via Toolbar Editor
                    }
                }
                catch (Exception iconEx)
                {
                    // Don't fail plugin load if icon creation fails
                    Rhino.RhinoApp.WriteLine($"PenguinClaw: Icon creation warning: {iconEx.Message}");
                }

                return LoadReturnCode.Success;
            }
            catch (Exception ex)
            {
                errorMessage = $"Failed to register panel: {ex.Message}";
                return LoadReturnCode.ErrorShowDialog;
            }
        }

        protected override void OnShutdown()
        {
            try { PenguinClawServer.StopServer(); } catch { }
            base.OnShutdown();
        }
    }
}
