using System;
using System.Linq;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace PenguinClaw
{
    public class PenguinClawToolsCommand : Command
    {
        public override string EnglishName => "PenguinClawTools";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var selected = doc.Objects.GetSelectedObjects(false, false).ToList();
            if (selected.Count == 0)
            {
                RhinoApp.WriteLine("PenguinClawTools: No objects selected.");
                return Result.Success;
            }

            RhinoApp.WriteLine($"PenguinClawTools: {selected.Count} objects selected.");
            foreach (var obj in selected)
            {
                double vol  = ComputeVolume(obj.Geometry);
                double area = ComputeArea(obj.Geometry);
                RhinoApp.WriteLine($"- ID: {obj.Id}  Type: {obj.Geometry.GetType().Name}  Layer: {obj.Attributes.LayerIndex}  Volume: {vol:F4}  Area: {area:F4}");
            }

            // Example: move first selected object by (1,1,1)
            var first = selected.First();
            var xform = Transform.Translation(new Vector3d(1, 1, 1));
            var newId = doc.Objects.Transform(first.Id, xform, true);
            if (newId != Guid.Empty)
            {
                doc.Views.Redraw();
                RhinoApp.WriteLine($"PenguinClawTools: Moved {first.Id}.");
            }
            else
            {
                RhinoApp.WriteLine("PenguinClawTools: Move failed.");
            }

            return Result.Success;
        }

        private static double ComputeVolume(GeometryBase geom)
        {
            try
            {
                VolumeMassProperties mp = null;
                if      (geom is Brep      b) mp = VolumeMassProperties.Compute(b);
                else if (geom is Mesh      m) mp = VolumeMassProperties.Compute(m);
                else if (geom is Extrusion e) mp = VolumeMassProperties.Compute(e);
                return mp?.Volume ?? 0.0;
            }
            catch { return 0.0; }
        }

        private static double ComputeArea(GeometryBase geom)
        {
            try
            {
                AreaMassProperties ap = null;
                if      (geom is Brep      b) ap = AreaMassProperties.Compute(b);
                else if (geom is Mesh      m) ap = AreaMassProperties.Compute(m);
                else if (geom is Surface   s) ap = AreaMassProperties.Compute(s);
                else if (geom is Extrusion e) ap = AreaMassProperties.Compute(e);
                return ap?.Area ?? 0.0;
            }
            catch { return 0.0; }
        }
    }
}
