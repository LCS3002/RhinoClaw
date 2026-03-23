using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Rhino;

namespace PenguinClaw
{
    /// <summary>
    /// Caches every Rhino command and GH component as an individual Claude tool definition.
    /// Built once on startup (background thread), refreshed when plugins load.
    /// Per request, GetRelevantTools() returns the top-K most relevant entries via keyword matching.
    /// </summary>
    internal static class RhinoCommandRegistry
    {
        private static List<CachedTool> _cache = new List<CachedTool>();
        private static readonly object _lock = new object();

        // GH component GUID lookup for canvas insertion: toolName → component GUID string
        private static readonly Dictionary<string, string> _ghGuidMap = new Dictionary<string, string>();

        private class CachedTool
        {
            public JObject Definition;
            public string[] Keywords; // pre-tokenised for fast scoring
        }

        // ── Public API ───────────────────────────────────────────────────────────

        public static int CachedCount { get { lock (_lock) { return _cache.Count; } } }
        public static int RhinoCommandCount { get { lock (_lock) { return _cache.Count(e => e.Definition["name"]?.ToString().StartsWith("rhino_cmd_") == true); } } }
        public static int GhComponentCount  { get { lock (_lock) { return _cache.Count(e => e.Definition["name"]?.ToString().StartsWith("gh_comp_")   == true); } } }

        /// <summary>Call from background thread on plugin load and after scan.</summary>
        public static void Build()
        {
            var entries = new List<CachedTool>();
            var guidMap = new Dictionary<string, string>();

            entries.AddRange(BuildRhinoCommandTools());
            entries.AddRange(BuildGhComponentTools(guidMap));

            lock (_lock)
            {
                _cache = entries;
                _ghGuidMap.Clear();
                foreach (var kv in guidMap)
                    _ghGuidMap[kv.Key] = kv.Value;
            }

            var rhinoCount = entries.Count(e => e.Definition["name"]?.ToString().StartsWith("rhino_cmd_") == true);
            var ghCount    = entries.Count(e => e.Definition["name"]?.ToString().StartsWith("gh_comp_")   == true);
            RhinoApp.WriteLine($"PenguinClaw: registry built — {rhinoCount} Rhino commands + {ghCount} GH components.");
        }

        /// <summary>
        /// Return the top-K GH component tools most relevant to userMessage.
        /// rhino_cmd_* tools are intentionally excluded — the base run_rhino_command
        /// tool already covers all Rhino commands and avoids sending hundreds of
        /// redundant definitions that change per-request and break prompt caching.
        /// </summary>
        public static List<JObject> GetRelevantTools(string userMessage, int topK = 10)
        {
            List<CachedTool> cache;
            lock (_lock) { cache = _cache; }
            if (cache.Count == 0) return new List<JObject>();

            var queryTokens = new HashSet<string>(Tokenize(userMessage), StringComparer.OrdinalIgnoreCase);
            if (queryTokens.Count == 0) return new List<JObject>();

            return cache
                .Where(t => t.Definition["name"]?.ToString().StartsWith("gh_comp_") == true)
                .Select(t => (tool: t, score: t.Keywords.Count(k => queryTokens.Contains(k))))
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .Take(topK)
                .Select(x => x.tool.Definition)
                .ToList();
        }

        /// <summary>Execute a dynamic tool (rhino_cmd_* or gh_comp_*). Called from PenguinClawTools.</summary>
        public static string ExecuteDynamic(string toolName, JObject input)
        {
            var args = input["args"]?.ToString()?.Trim() ?? "";

            if (toolName.StartsWith("rhino_cmd_"))
            {
                // Recover the original Rhino command name from the description
                var originalName = GetOriginalRhinoName(toolName);
                var cmd = string.IsNullOrEmpty(args)
                    ? $"_{originalName}"
                    : $"_{originalName} {args}";
                return PenguinClawTools.RunCommandInternal(cmd);
            }

            if (toolName.StartsWith("gh_comp_"))
            {
                string guid;
                lock (_lock) { _ghGuidMap.TryGetValue(toolName, out guid); }
                return AddGhComponent(toolName, guid, args);
            }

            return Fail("Unknown dynamic tool: " + toolName);
        }

        // ── Rhino commands ───────────────────────────────────────────────────────

        private static List<CachedTool> BuildRhinoCommandTools()
        {
            var result = new List<CachedTool>();
            IEnumerable<string> names;

            try   { names = Rhino.Commands.Command.GetCommandNames(true, true) ?? new string[0]; }
            catch { names = _fallbackCommands.Select(x => x.Item1); }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawName in names)
            {
                if (string.IsNullOrWhiteSpace(rawName)) continue;
                if (!seen.Add(rawName)) continue;

                var safeName = ToolName("rhino_cmd_", rawName);
                var desc     = GetRhinoDescription(rawName);
                result.Add(MakeTool(safeName, $"Rhino command: {rawName}. {desc}", rawName + " " + desc));
            }
            return result;
        }

        // ── GH components ────────────────────────────────────────────────────────

        private static List<CachedTool> BuildGhComponentTools(Dictionary<string, string> guidMap)
        {
            var result = new List<CachedTool>();
            try
            {
                var ghAsm      = Assembly.Load("Grasshopper");
                var instances  = ghAsm.GetType("Grasshopper.Instances");
                var serverProp = instances?.GetProperty("ComponentServer", BindingFlags.Public | BindingFlags.Static);
                var server     = serverProp?.GetValue(null);
                var proxies    = server?.GetType()
                    .GetProperty("ObjectProxies", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(server) as IEnumerable;
                if (proxies == null) return result;

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var proxy in proxies)
                {
                    if (proxy == null) continue;
                    var pt = proxy.GetType();
                    string Get(string p) => pt.GetProperty(p, BindingFlags.Public | BindingFlags.Instance)
                                               ?.GetValue(proxy)?.ToString() ?? "";
                    var name = Get("Name");
                    var cat  = Get("Category");
                    var sub  = Get("SubCategory");
                    var desc = Get("Desc");
                    var guid = Get("Guid");
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    // Deduplicate: append category abbreviation if name collides
                    var key = name;
                    if (!seen.Add(key))
                        key = name + "_" + (cat.Length > 0 ? cat[0].ToString() : "x");

                    var safeName = ToolName("gh_comp_", key);
                    var fullDesc = $"Grasshopper component: {name} ({cat} › {sub}). {desc}";
                    result.Add(MakeTool(safeName, fullDesc, $"{name} {cat} {sub} {desc} grasshopper gh"));

                    if (!string.IsNullOrWhiteSpace(guid))
                        guidMap[safeName] = guid;
                }
            }
            catch { /* GH not loaded */ }
            return result;
        }

        // ── GH component insertion ───────────────────────────────────────────────

        private static string AddGhComponent(string toolName, string componentGuid, string args)
        {
            try
            {
                var ghAsm      = Assembly.Load("Grasshopper");
                var instances  = ghAsm.GetType("Grasshopper.Instances");
                var serverProp = instances?.GetProperty("ComponentServer", BindingFlags.Public | BindingFlags.Static);
                var server     = serverProp?.GetValue(null);

                // Find proxy by GUID
                object proxy = null;
                if (!string.IsNullOrEmpty(componentGuid))
                {
                    var proxies = server?.GetType()
                        .GetProperty("ObjectProxies", BindingFlags.Public | BindingFlags.Instance)
                        ?.GetValue(server) as IEnumerable;
                    if (proxies != null)
                    {
                        var guidParsed = Guid.Parse(componentGuid);
                        foreach (var p in proxies)
                        {
                            var pg = p?.GetType().GetProperty("Guid", BindingFlags.Public | BindingFlags.Instance)?.GetValue(p);
                            if (pg is Guid g && g == guidParsed) { proxy = p; break; }
                        }
                    }
                }

                if (proxy == null)
                    return Fail($"GH component proxy not found for {toolName}. Make sure Grasshopper is open.");

                // Create an instance of the component
                var createMethod = proxy.GetType().GetMethod("CreateInstance",
                    BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                var component = createMethod?.Invoke(proxy, null);
                if (component == null)
                    return Fail("Could not create component instance.");

                // Get active GH document
                var canvasProp = instances?.GetProperty("ActiveCanvas", BindingFlags.Public | BindingFlags.Static);
                var canvas     = canvasProp?.GetValue(null);
                var docProp    = canvas?.GetType().GetProperty("Document", BindingFlags.Public | BindingFlags.Instance);
                var ghDoc      = docProp?.GetValue(canvas);
                if (ghDoc == null)
                    return Fail("No active Grasshopper canvas. Open Grasshopper first.");

                // Add to document
                var addMethod = ghDoc.GetType().GetMethod("AddObject",
                    new[] { component.GetType().BaseType ?? component.GetType(), typeof(bool) })
                    ?? ghDoc.GetType().GetMethod("AddObject",
                    new[] { typeof(object), typeof(bool) });
                addMethod?.Invoke(ghDoc, new[] { component, false });

                // Trigger a new solution
                var newSolution = ghDoc.GetType().GetMethod("NewSolution",
                    new[] { typeof(bool) });
                newSolution?.Invoke(ghDoc, new object[] { false });

                var compName = proxy.GetType()
                    .GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(proxy)?.ToString() ?? toolName;

                return new JObject
                {
                    ["success"] = true,
                    ["message"] = $"Added '{compName}' to the Grasshopper canvas.",
                    ["component"] = compName,
                }.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch (Exception ex)
            {
                return Fail($"Failed to add GH component: {ex.Message}");
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static CachedTool MakeTool(string name, string description, string keywords)
        {
            var def = new JObject
            {
                ["name"]         = name,
                ["description"]  = description,
                ["input_schema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["args"] = new JObject
                        {
                            ["type"]        = "string",
                            ["description"] = "Optional arguments / parameters (e.g. coordinates, values).",
                        },
                    },
                    ["required"] = new JArray(),
                },
            };
            return new CachedTool { Definition = def, Keywords = Tokenize(keywords) };
        }

        /// <summary>Produce a safe Claude tool name (a-z, A-Z, 0-9, _, max 64 chars).</summary>
        private static string ToolName(string prefix, string raw)
        {
            var s    = new string(raw.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
            var full = prefix + s;
            return full.Length > 64 ? full.Substring(0, 64) : full;
        }

        private static string[] Tokenize(string text)
        {
            if (string.IsNullOrEmpty(text)) return new string[0];
            return text.Split(new[] { ' ', '_', '-', '.', '/', '(', ')', ',', '>', '<', '\n', '\r', '›' },
                              StringSplitOptions.RemoveEmptyEntries)
                       .Select(t => t.ToLowerInvariant())
                       .Where(t => t.Length >= 3)
                       .Distinct()
                       .ToArray();
        }

        private static string GetOriginalRhinoName(string toolName)
        {
            // Recover from the tool description stored in the cache
            List<CachedTool> cache;
            lock (_lock) { cache = _cache; }
            var entry = cache.FirstOrDefault(t => t.Definition["name"]?.ToString() == toolName);
            var desc  = entry?.Definition["description"]?.ToString() ?? "";
            // Format: "Rhino command: {Name}. ..."
            const string prefix = "Rhino command: ";
            if (desc.StartsWith(prefix))
            {
                var rest = desc.Substring(prefix.Length);
                var dot  = rest.IndexOf('.');
                return dot > 0 ? rest.Substring(0, dot) : rest;
            }
            // Fallback: strip prefix from tool name
            return toolName.Substring("rhino_cmd_".Length);
        }

        private static string Fail(string msg) =>
            new JObject { ["success"] = false, ["message"] = msg }.ToString(Newtonsoft.Json.Formatting.None);

        // ── Command descriptions ──────────────────────────────────────────────────

        private static string GetRhinoDescription(string cmd) =>
            _descriptions.TryGetValue(cmd, out var d) ? d : $"Executes the Rhino {cmd} command.";

        private static readonly Dictionary<string, string> _descriptions =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Box"]                 = "Creates a box. args: '<firstCorner> <baseDiagonalCorner> <height>' — e.g. '0,0,0 10,10,0 10' makes a 10×10×10 box at origin. The base diagonal is on the XY plane, height is separate.",
            ["Sphere"]              = "Creates a sphere. args: '<center> <radius>' — e.g. '0,0,0 5' makes a sphere of radius 5 at origin.",
            ["Cylinder"]            = "Creates a cylinder. args: '<baseCenter> <radius> <height>' — e.g. '0,0,0 3 10' makes radius-3 cylinder of height 10 at origin.",
            ["Cone"]                = "Creates a cone. args: '<baseCenter> <radius> <height>' — e.g. '0,0,0 5 10'.",
            ["Torus"]               = "Creates a torus. args: '<center> <majorRadius> <minorRadius>' — e.g. '0,0,0 10 2'.",
            ["Ellipsoid"]           = "Creates an ellipsoid.",
            ["Plane"]               = "Creates a plane surface.",
            ["PlanarSrf"]           = "Creates planar surfaces from selected boundary curves.",
            ["ExtrudeCrv"]          = "Extrudes a curve to create a surface. Select curve, specify distance.",
            ["ExtrudeSrf"]          = "Extrudes a surface. Select surface, specify distance.",
            ["Extrude"]             = "Extrudes curves or surfaces.",
            ["Loft"]                = "Creates a lofted surface through a series of profile curves.",
            ["Sweep1"]              = "Sweeps a profile along a single rail curve.",
            ["Sweep2"]              = "Sweeps a profile along two rail curves.",
            ["Revolve"]             = "Revolves a curve around an axis to create a surface.",
            ["NetworkSrf"]          = "Creates a surface from a network of curves.",
            ["Patch"]               = "Fits a surface to selected curves or points.",
            ["BooleanUnion"]        = "Boolean union — combines two or more solids into one.",
            ["BooleanDifference"]   = "Boolean difference — subtracts one or more solids from another.",
            ["BooleanIntersection"] = "Boolean intersection — keeps only the shared volume of solids.",
            ["BooleanSplit"]        = "Splits Breps with other Breps.",
            ["FilletEdge"]          = "Adds a round fillet to Brep edges. Specify radius.",
            ["ChamferEdge"]         = "Adds a chamfer to Brep edges. Specify distance.",
            ["Shell"]               = "Hollows out a solid, creating a thin-walled shell.",
            ["Move"]                = "Moves selected objects. Select first, then args: '<fromPoint> <toPoint>' — e.g. '0,0,0 0,0,5' moves 5 units up.",
            ["Copy"]                = "Copies selected objects. Select first, then args: '<fromPoint> <toPoint>'.",
            ["Rotate"]              = "Rotates selected objects. args: '<centerPoint> <angle>' — e.g. '0,0,0 45' rotates 45°.",
            ["Rotate3D"]            = "Rotates selected objects around an arbitrary 3D axis. args: '<axisStart> <axisEnd> <angle>'.",
            ["Scale"]               = "Scales selected objects uniformly. args: '<basePoint> <scaleFactor>' — e.g. '0,0,0 2' doubles size.",
            ["Scale1D"]             = "Scales objects in one direction.",
            ["Scale2D"]             = "Scales objects in two directions.",
            ["Mirror"]              = "Mirrors objects across a plane.",
            ["Array"]               = "Creates a rectangular grid array of objects.",
            ["ArrayLinear"]         = "Creates a linear array of objects.",
            ["ArrayPolar"]          = "Creates a polar (radial) array of objects.",
            ["ArrayCrv"]            = "Arrays objects along a curve.",
            ["Delete"]              = "Deletes the currently selected objects.",
            ["Undo"]                = "Undoes the last modelling action.",
            ["Redo"]                = "Redoes the last undone action.",
            ["Join"]                = "Joins curves, surfaces, or polysurfaces into one object.",
            ["Explode"]             = "Explodes polycurves, polysurfaces, or meshes into components.",
            ["Split"]               = "Splits objects using a cutting object or curve.",
            ["Trim"]                = "Trims away parts of curves or surfaces.",
            ["Extend"]              = "Extends curves to a boundary or by a distance.",
            ["Fillet"]              = "Creates a tangent arc between two curves.",
            ["Chamfer"]             = "Creates a straight line chamfer between two curves.",
            ["Offset"]              = "Offsets curves or surfaces by a distance.",
            ["OffsetCrv"]           = "Offsets a curve by a specified distance.",
            ["OffsetSrf"]           = "Offsets a surface by a specified distance.",
            ["Project"]             = "Projects curves onto surfaces along the construction plane normal.",
            ["Intersect"]           = "Creates curves at the intersection of two surfaces.",
            ["DupEdge"]             = "Duplicates edges of a Brep as curves.",
            ["DupBorder"]           = "Duplicates the border of a surface or Brep.",
            ["Contour"]             = "Creates equally-spaced contour curves on a surface or mesh.",
            ["Section"]             = "Creates section curves where a cutting plane intersects geometry.",
            ["MeshFromSurface"]     = "Creates a polygon mesh from a surface.",
            ["MeshBooleanUnion"]    = "Boolean union on meshes.",
            ["MeshBooleanDifference"] = "Boolean difference on meshes.",
            ["Silhouette"]          = "Creates silhouette curves for selected objects.",
            ["Layer"]               = "Opens the Layer panel for managing layers.",
            ["Properties"]          = "Opens the Properties panel for selected objects.",
            ["Group"]               = "Groups selected objects together.",
            ["Ungroup"]             = "Ungroups the selected group.",
            ["Lock"]                = "Locks selected objects so they cannot be selected.",
            ["Unlock"]              = "Unlocks all locked objects.",
            ["Hide"]                = "Hides selected objects.",
            ["Show"]                = "Shows all hidden objects.",
            ["SelAll"]              = "Selects all objects in the document.",
            ["SelNone"]             = "Deselects all objects.",
            ["SelLast"]             = "Selects the most recently created object.",
            ["SelLayer"]            = "Selects all objects on a specified layer.",
            ["Invert"]              = "Inverts the current selection.",
            ["ZoomExtents"]         = "Zooms all viewports to show all objects.",
            ["ZoomSelected"]        = "Zooms to fit the selected objects in the viewport.",
            ["Render"]              = "Renders the current scene using the active renderer.",
            ["Grasshopper"]         = "Opens (or focuses) the Grasshopper visual programming editor.",
            ["GrasshopperPlayer"]   = "Runs a Grasshopper definition file (.gh or .ghx).",
            ["Import"]              = "Imports geometry from an external file.",
            ["Export"]              = "Exports selected objects to a file.",
            ["SaveAs"]              = "Saves the document with a new name.",
            ["Units"]               = "Sets or queries the document measurement units.",
            ["DocumentProperties"]  = "Opens the Document Properties dialog.",
            ["Hatch"]               = "Fills a closed curve boundary with a hatch pattern.",
            ["Text"]                = "Creates a text annotation object.",
            ["Leader"]              = "Creates an annotation leader with text.",
            ["Polyline"]            = "Creates a polyline through clicked points.",
            ["Line"]                = "Creates a straight line between two points.",
            ["Arc"]                 = "Creates an arc curve.",
            ["Circle"]              = "Creates a circle. args: '<center> <radius>' — e.g. '0,0,0 5'.",
            ["Ellipse"]             = "Creates an ellipse. args: '<center> <radiusX> <radiusY>'.",
            ["Rectangle"]           = "Creates a rectangle. args: '<firstCorner> <secondCorner>' or '<firstCorner> <width> <height>'.",
            ["Polygon"]             = "Creates a regular polygon.",
            ["Spiral"]              = "Creates a flat or 3D spiral curve.",
            ["Helix"]               = "Creates a helical curve.",
            ["Curve"]               = "Creates a free-form NURBS curve by clicking control points.",
            ["InterpCrv"]           = "Creates a curve that passes exactly through clicked points.",
            ["Points"]              = "Creates individual point objects.",
            ["Polyline"]            = "Creates a polyline through clicked points.",
            ["SubD"]                = "Creates SubD (subdivision surface) geometry.",
            ["SubDBox"]             = "Creates a SubD box primitive.",
            ["SubDSphere"]          = "Creates a SubD sphere primitive.",
            ["QuadRemesh"]          = "Remeshes geometry as a quad-dominant mesh.",
            ["Weld"]                = "Welds mesh vertices within a tolerance.",
            ["ReduceMesh"]          = "Reduces the polygon count of a mesh.",
            ["Smooth"]              = "Smooths selected objects.",
            ["SetPt"]               = "Sets the X, Y, or Z coordinate of points.",
            ["Rebuild"]             = "Rebuilds curves or surfaces with a new point count and degree.",
            ["FitCrv"]              = "Fits a new curve to an existing curve.",
            ["ChangeDegree"]        = "Changes the degree of a curve or surface.",
            ["InsertKnot"]          = "Inserts a knot into a NURBS curve or surface.",
            ["RemoveKnot"]          = "Removes a knot from a NURBS curve or surface.",
            ["MakeUniform"]         = "Makes the knot vector of a curve or surface uniform.",
            ["Cap"]                 = "Adds planar caps to open polysurface openings.",
            ["MergeAllFaces"]       = "Merges all co-planar faces of a Brep.",
            ["ShrinkTrimmedSrf"]    = "Shrinks trimmed surfaces to their trim boundaries.",
            ["UnrollSrf"]           = "Unrolls a developable surface flat.",
            ["Squish"]              = "Flattens a surface by squishing it.",
            ["Smash"]               = "Flattens surfaces like Squish.",
            ["Divide"]              = "Divides a curve into segments and places points.",
            ["Shorten"]             = "Shortens curves by a distance from each end.",
            ["Twist"]               = "Twists objects around an axis.",
            ["Bend"]                = "Bends objects along a curve.",
            ["Taper"]               = "Tapers objects.",
            ["Flow"]                = "Morphs objects along a curve (Flow along curve).",
            ["Sporph"]              = "Morphs objects from one surface to another.",
            ["Cage"]                = "Creates a cage deformer around objects.",
            ["CageEdit"]            = "Deforms objects using control points of a cage.",
            ["SoftEditCrv"]         = "Soft-edits a curve by moving control points with falloff.",
            ["SoftEditSrf"]         = "Soft-edits a surface by moving control points with falloff.",
            ["DraftAngleAnalysis"]  = "Analyses draft angles on surfaces.",
            ["CurvatureAnalysis"]   = "Displays curvature analysis on surfaces.",
            ["ZebraAnalysis"]       = "Displays zebra stripe continuity analysis on surfaces.",
            ["EMap"]                = "Displays environment map reflection on surfaces.",
            ["Thickness"]           = "Measures the thickness of objects.",
            ["VolumeCentroid"]      = "Calculates the volume centroid of closed objects.",
            ["Area"]                = "Reports the area of selected surfaces, Breps, or Hatches.",
            ["Volume"]              = "Reports the volume of selected closed Breps or Meshes.",
            ["Length"]              = "Reports the length of selected curves.",
            ["Distance"]            = "Reports the distance between two picked points.",
            ["Angle"]               = "Reports the angle between two lines.",
            ["BoundingBox"]         = "Creates a bounding box around selected objects.",
            ["NamedView"]           = "Manages named views (save/restore camera positions).",
            ["SetView"]             = "Sets the viewport to a standard view (Top, Front, Right, Perspective).",
            ["CPlane"]              = "Sets or modifies the construction plane.",
            ["Grid"]                = "Controls the grid display settings.",
            ["Lights"]              = "Manages scene lights.",
            ["Sun"]                 = "Controls the sun light settings.",
            ["Material"]            = "Assigns or edits materials on objects.",
            ["PictureFrame"]        = "Creates a picture frame with an embedded image.",
            ["TextObject"]          = "Creates 3D text geometry.",
            ["Worksession"]         = "Manages worksession (multi-file project) settings.",
            ["BlockEdit"]           = "Edits a block definition in place.",
            ["Block"]               = "Creates a block definition from selected objects.",
            ["Insert"]              = "Inserts a block instance or imports a file as a block.",
            ["Explode"]             = "Explodes block instances or polysurfaces into components.",
        };

        private static readonly (string, string)[] _fallbackCommands =
        {
            ("Box","Creates a box."), ("Sphere","Creates a sphere."), ("Cylinder","Creates a cylinder."),
            ("Cone","Creates a cone."), ("Torus","Creates a torus."), ("Loft","Lofts curves."),
            ("ExtrudeCrv","Extrudes a curve."), ("Revolve","Revolves around axis."),
            ("BooleanUnion","Boolean union of solids."), ("BooleanDifference","Boolean difference."),
            ("BooleanIntersection","Boolean intersection."), ("FilletEdge","Fillets Brep edges."),
            ("Shell","Hollows a solid."), ("Move","Moves objects."), ("Copy","Copies objects."),
            ("Rotate","Rotates objects."), ("Scale","Scales objects."), ("Mirror","Mirrors objects."),
            ("Array","Rectangular array."), ("ArrayPolar","Polar array."), ("Delete","Deletes objects."),
            ("Join","Joins objects."), ("Explode","Explodes objects."), ("Trim","Trims curves/surfaces."),
            ("Offset","Offsets curves/surfaces."), ("Project","Projects curves to surfaces."),
            ("Intersect","Intersection curves."), ("Circle","Creates a circle."), ("Line","Creates a line."),
            ("Arc","Creates an arc."), ("Curve","Free-form curve."), ("InterpCrv","Interpolated curve."),
            ("Polyline","Creates a polyline."), ("Rectangle","Creates a rectangle."),
            ("Grasshopper","Opens Grasshopper."), ("Render","Renders the scene."),
            ("Layer","Layer manager."), ("Group","Groups objects."), ("Hide","Hides objects."),
            ("Show","Shows hidden objects."), ("SelAll","Selects all objects."),
            ("ZoomExtents","Zooms to all objects."), ("Undo","Undo."), ("Redo","Redo."),
        };
    }
}
