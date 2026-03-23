using System.Drawing;
using Rhino;
using Rhino.Commands;
using Rhino.UI;

namespace PenguinClaw
{
    public class PenguinClawCommand : Command
    {
        public override string EnglishName => "PenguinClaw";
        public override string LocalName   => "PenguinClaw";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            PenguinClawServer.StartServer();
            Panels.OpenPanel(PenguinClawPanel.PanelId);
            return Result.Success;
        }

        public static Bitmap CreatePenguinClawIcon(int size = 24) =>
            PenguinClawIconFactory.CreateBitmap(size);
    }
}
