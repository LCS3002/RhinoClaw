using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rhino;
using Rhino.Geometry;

namespace PenguinClaw
{
    internal static class PenguinClawTools
    {
        // ── Tool definitions (Anthropic format) ─────────────────────────────────

        public static JArray GetToolDefinitions()
        {
            return new JArray
            {
                new JObject
                {
                    ["name"]        = "get_selected_objects",
                    ["description"] = "Lists all currently selected objects in the Rhino document with their IDs, geometry type, and layer.",
                    ["input_schema"] = new JObject { ["type"] = "object", ["properties"] = new JObject(), ["required"] = new JArray() },
                },
                new JObject
                {
                    ["name"]        = "select_objects_by_id",
                    ["description"] = "Selects one or more objects in Rhino by their GUID, replacing the current selection. " +
                                      "Always call this before any command that operates on specific objects (Move, Delete, Scale, Rotate, BooleanUnion, etc.) " +
                                      "so the right objects are selected regardless of what the user may have clicked since.",
                    ["input_schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["ids"] = new JObject
                            {
                                ["type"]        = "array",
                                ["items"]       = new JObject { ["type"] = "string" },
                                ["description"] = "List of object GUIDs to select.",
                            },
                        },
                        ["required"] = new JArray { "ids" },
                    },
                },
                new JObject
                {
                    ["name"]        = "get_object_info",
                    ["description"] = "Returns detailed info about a specific Rhino object: geometry type, layer, name, volume, area, and bounding box.",
                    ["input_schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["object_id"] = new JObject { ["type"] = "string", ["description"] = "GUID of the object." },
                        },
                        ["required"] = new JArray { "object_id" },
                    },
                },
                new JObject
                {
                    ["name"]        = "get_volume",
                    ["description"] = "Calculates the volume of a Rhino Brep or Mesh in document units³.",
                    ["input_schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["object_id"] = new JObject { ["type"] = "string", ["description"] = "GUID of the object." },
                        },
                        ["required"] = new JArray { "object_id" },
                    },
                },
                new JObject
                {
                    ["name"]        = "move_object",
                    ["description"] = "Translates a Rhino object by a (x, y, z) vector in document units.",
                    ["input_schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["object_id"] = new JObject { ["type"] = "string", ["description"] = "GUID of the object." },
                            ["x"]         = new JObject { ["type"] = "number", ["description"] = "X translation." },
                            ["y"]         = new JObject { ["type"] = "number", ["description"] = "Y translation." },
                            ["z"]         = new JObject { ["type"] = "number", ["description"] = "Z translation." },
                        },
                        ["required"] = new JArray { "object_id", "x", "y", "z" },
                    },
                },
                new JObject
                {
                    ["name"]        = "scale_object",
                    ["description"] = "Scales a Rhino object uniformly in-place by a factor. Use this whenever the user asks to scale, resize, make bigger/smaller, or change the size of an existing object. Never create new geometry to achieve scaling. factor=2 doubles size, factor=0.5 halves it.",
                    ["input_schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["object_id"] = new JObject { ["type"] = "string", ["description"] = "GUID of the object to scale." },
                            ["factor"]    = new JObject { ["type"] = "number", ["description"] = "Uniform scale factor. 2 = double size, 0.5 = half size." },
                            ["base_x"]    = new JObject { ["type"] = "number", ["description"] = "X coordinate of the base point to scale from (default 0)." },
                            ["base_y"]    = new JObject { ["type"] = "number", ["description"] = "Y coordinate of the base point to scale from (default 0)." },
                            ["base_z"]    = new JObject { ["type"] = "number", ["description"] = "Z coordinate of the base point to scale from (default 0)." },
                        },
                        ["required"] = new JArray { "object_id", "factor" },
                    },
                },
                new JObject
                {
                    ["name"]        = "list_layers",
                    ["description"] = "Lists all layers in the Rhino document with name, visibility, and lock state.",
                    ["input_schema"] = new JObject { ["type"] = "object", ["properties"] = new JObject(), ["required"] = new JArray() },
                },
                new JObject
                {
                    ["name"]        = "list_gh_sliders",
                    ["description"] = "Lists all number sliders on the active Grasshopper canvas with name, current value, min, and max.",
                    ["input_schema"] = new JObject { ["type"] = "object", ["properties"] = new JObject(), ["required"] = new JArray() },
                },
                new JObject
                {
                    ["name"]        = "list_gh_components",
                    ["description"] = "Lists all components on the active Grasshopper canvas with name, type, and instance GUID.",
                    ["input_schema"] = new JObject { ["type"] = "object", ["properties"] = new JObject(), ["required"] = new JArray() },
                },
                new JObject
                {
                    ["name"]        = "search_gh_components",
                    ["description"] = "Searches the Grasshopper component server for components matching a keyword. Returns name and GUID. Use this to find the exact component_name or GUID before calling build_gh_definition.",
                    ["input_schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["keyword"] = new JObject { ["type"] = "string", ["description"] = "Search term (case-insensitive, partial match). E.g. 'python', 'sphere', 'script'." },
                        },
                        ["required"] = new JArray { "keyword" },
                    },
                },
                new JObject
                {
                    ["name"]        = "set_gh_slider",
                    ["description"] = "Sets the value of a Grasshopper number slider by NickName and triggers a new solution.",
                    ["input_schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["name"]  = new JObject { ["type"] = "string", ["description"] = "NickName of the slider." },
                            ["value"] = new JObject { ["type"] = "number", ["description"] = "New value (must be within slider min/max)." },
                        },
                        ["required"] = new JArray { "name", "value" },
                    },
                },
                new JObject
                {
                    ["name"]        = "capture_viewport",
                    ["description"] = "Captures the active Rhino viewport as a PNG and returns the file path.",
                    ["input_schema"] = new JObject { ["type"] = "object", ["properties"] = new JObject(), ["required"] = new JArray() },
                },
                new JObject
                {
                    ["name"]        = "run_rhino_command",
                    ["description"] = "Runs a Rhino command string exactly as if typed in the Rhino command line. " +
                                      "Use for view/layer/selection commands and advanced surface ops (Loft, Sweep1, FilletEdge, Revolve, ExtrudeCrv, Cap, Shell, Rebuild). " +
                                      "NOT for creating geometry — use execute_python_code with rhinoscriptsyntax instead (rs.AddSphere, rs.AddBox, rs.AddCylinder, etc.). " +
                                      "For commands that need object selection, call get_selected_objects first. " +
                                      "Returns success/failure and any output text from the command.",
                    ["input_schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["command"] = new JObject { ["type"] = "string", ["description"] = "The full Rhino command string to execute, e.g. '_Box 0,0,0 10,10,10 _Enter' or '_Sphere 0,0,0 5'." },
                            ["echo"]    = new JObject { ["type"] = "boolean", ["description"] = "Whether to echo command to the Rhino command line (default true)." },
                        },
                        ["required"] = new JArray { "command" },
                    },
                },
                new JObject
                {
                    ["name"]        = "get_document_summary",
                    ["description"] = "Returns a high-level overview of the entire Rhino document: object counts by type and by layer, scene bounding box, active layer, and document name. Call this at the start of any session or when you need to understand what is in the scene.",
                    ["input_schema"] = new JObject { ["type"] = "object", ["properties"] = new JObject(), ["required"] = new JArray() },
                },
                new JObject
                {
                    ["name"]        = "get_scene_layout",
                    ["description"] = "Returns every object in the scene with its ID, type, name, layer, bounding_box [xmin,ymin,zmin,xmax,ymax,zmax] and center point. " +
                                      "Use this before any spatial placement task (\"put X on Y\", \"next to\", \"above\") to know exactly where existing objects are. " +
                                      "Much faster than calling get_object_info repeatedly.",
                    ["input_schema"] = new JObject { ["type"] = "object", ["properties"] = new JObject(), ["required"] = new JArray() },
                },
                new JObject
                {
                    ["name"]        = "place_object",
                    ["description"] = "Moves an object so that a chosen anchor point of its bounding box lands at the target coordinates. " +
                                      "anchor: 'base' = bottom-center (use to sit something on a surface), " +
                                      "'center' = geometric center, 'min' = bbox min corner. " +
                                      "target_x/y/z: the world coordinates the anchor should land on. " +
                                      "Omit axes you don't want to change (they stay as-is). " +
                                      "Example: place_object(id, anchor='base', target_z=2.5) lifts the object so its base is exactly at Z=2.5.",
                    ["input_schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["object_id"] = new JObject { ["type"] = "string",  ["description"] = "GUID of the object to move." },
                            ["anchor"]    = new JObject { ["type"] = "string",  ["description"] = "'base' (bottom-center), 'center', or 'min' (bbox min corner). Default: 'base'." },
                            ["target_x"]  = new JObject { ["type"] = "number",  ["description"] = "Target world X. Omit to leave X unchanged." },
                            ["target_y"]  = new JObject { ["type"] = "number",  ["description"] = "Target world Y. Omit to leave Y unchanged." },
                            ["target_z"]  = new JObject { ["type"] = "number",  ["description"] = "Target world Z. Omit to leave Z unchanged." },
                        },
                        ["required"] = new JArray { "object_id" },
                    },
                },
                new JObject
                {
                    ["name"]        = "delete_object",
                    ["description"] = "Deletes a Rhino object by ID. Supports Ctrl+Z undo.",
                    ["input_schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["object_id"] = new JObject { ["type"] = "string", ["description"] = "GUID of the object to delete." },
                        },
                        ["required"] = new JArray { "object_id" },
                    },
                },
                new JObject
                {
                    ["name"]        = "rename_object",
                    ["description"] = "Sets the name of a Rhino object. Useful for labelling geometry for future reference.",
                    ["input_schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["object_id"] = new JObject { ["type"] = "string", ["description"] = "GUID of the object." },
                            ["name"]      = new JObject { ["type"] = "string", ["description"] = "New name for the object." },
                        },
                        ["required"] = new JArray { "object_id", "name" },
                    },
                },
                new JObject
                {
                    ["name"]        = "create_layer",
                    ["description"] = "Creates a new layer in the Rhino document. Does nothing if the layer already exists.",
                    ["input_schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["name"] = new JObject { ["type"] = "string", ["description"] = "Layer name (use :: for sub-layers, e.g. 'Structure::Columns')." },
                        },
                        ["required"] = new JArray { "name" },
                    },
                },
                new JObject
                {
                    ["name"]        = "set_current_layer",
                    ["description"] = "Sets the active layer in Rhino so new objects are created on it.",
                    ["input_schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["name"] = new JObject { ["type"] = "string", ["description"] = "Name of the layer to make current." },
                        },
                        ["required"] = new JArray { "name" },
                    },
                },
                new JObject
                {
                    ["name"]        = "undo",
                    ["description"] = "Undoes the last N operations in Rhino (default 1). Use when the user asks to undo, go back, or revert a step.",
                    ["input_schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["steps"] = new JObject { ["type"] = "integer", ["description"] = "Number of steps to undo (default 1)." },
                        },
                        ["required"] = new JArray(),
                    },
                },
                new JObject
                {
                    ["name"]        = "redo",
                    ["description"] = "Redoes the last N undone operations in Rhino (default 1).",
                    ["input_schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["steps"] = new JObject { ["type"] = "integer", ["description"] = "Number of steps to redo (default 1)." },
                        },
                        ["required"] = new JArray(),
                    },
                },
                new JObject
                {
                    ["name"]        = "execute_python_code",
                    ["description"] = "PRIMARY tool for geometry creation and complex operations. " +
                                      "ALWAYS use this (not run_rhino_command) to create geometry: rs.AddSphere, rs.AddBox, rs.AddCylinder, rs.AddCone, rs.AddTorus, rs.AddLine, rs.AddCircle, etc. " +
                                      "Set colour immediately: rs.ObjectColor(id, (255, 140, 0)). " +
                                      "The variable 'doc' is pre-set to the active RhinoDocument. " +
                                      "Use 'import rhinoscriptsyntax as rs' for high-level operations; 'import Rhino' for RhinoCommon. " +
                                      "Always print() the resulting IDs so they appear in output.",
                    ["input_schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["code"] = new JObject { ["type"] = "string", ["description"] = "Python code to execute. Use print() to produce output." },
                        },
                        ["required"] = new JArray { "code" },
                    },
                },
                new JObject
                {
                    ["name"]        = "rotate_object",
                    ["description"] = "Rotates a Rhino object around an axis through a center point. Supports Ctrl+Z undo.",
                    ["input_schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["object_id"]     = new JObject { ["type"] = "string", ["description"] = "GUID of the object." },
                            ["angle_degrees"] = new JObject { ["type"] = "number", ["description"] = "Rotation angle in degrees (positive = counter-clockwise when viewed from axis tip)." },
                            ["axis"]          = new JObject { ["type"] = "string", ["description"] = "Rotation axis: 'x', 'y', or 'z' (default 'z')." },
                            ["center_x"]      = new JObject { ["type"] = "number", ["description"] = "X of rotation center (default 0)." },
                            ["center_y"]      = new JObject { ["type"] = "number", ["description"] = "Y of rotation center (default 0)." },
                            ["center_z"]      = new JObject { ["type"] = "number", ["description"] = "Z of rotation center (default 0)." },
                        },
                        ["required"] = new JArray { "object_id", "angle_degrees" },
                    },
                },
                new JObject
                {
                    ["name"]        = "mirror_object",
                    ["description"] = "Mirrors a Rhino object across a world plane. Keeps the original.",
                    ["input_schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["object_id"]    = new JObject { ["type"] = "string", ["description"] = "GUID of the object." },
                            ["mirror_plane"] = new JObject { ["type"] = "string", ["description"] = "'xy', 'xz', or 'yz'." },
                            ["plane_offset"] = new JObject { ["type"] = "number", ["description"] = "Offset of the mirror plane from world origin along its normal (default 0)." },
                        },
                        ["required"] = new JArray { "object_id", "mirror_plane" },
                    },
                },
                new JObject
                {
                    ["name"]        = "array_linear",
                    ["description"] = "Creates N copies of an object, each offset by (dx, dy, dz) from the previous.",
                    ["input_schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["object_id"] = new JObject { ["type"] = "string", ["description"] = "GUID of the object to array." },
                            ["dx"]        = new JObject { ["type"] = "number", ["description"] = "X offset per copy." },
                            ["dy"]        = new JObject { ["type"] = "number", ["description"] = "Y offset per copy." },
                            ["dz"]        = new JObject { ["type"] = "number", ["description"] = "Z offset per copy." },
                            ["count"]     = new JObject { ["type"] = "integer", ["description"] = "Number of copies to create (not including original)." },
                        },
                        ["required"] = new JArray { "object_id", "dx", "dy", "dz", "count" },
                    },
                },
                new JObject
                {
                    ["name"]        = "array_polar",
                    ["description"] = "Creates N copies of an object arranged in a circular/arc pattern around a center point.",
                    ["input_schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["object_id"]   = new JObject { ["type"] = "string", ["description"] = "GUID of the object to array." },
                            ["count"]       = new JObject { ["type"] = "integer", ["description"] = "Number of copies (not including original)." },
                            ["total_angle"] = new JObject { ["type"] = "number", ["description"] = "Total angular sweep in degrees (360 = full circle)." },
                            ["center_x"]    = new JObject { ["type"] = "number", ["description"] = "X of rotation center (default 0)." },
                            ["center_y"]    = new JObject { ["type"] = "number", ["description"] = "Y of rotation center (default 0)." },
                            ["center_z"]    = new JObject { ["type"] = "number", ["description"] = "Z of rotation center (default 0)." },
                            ["axis"]        = new JObject { ["type"] = "string", ["description"] = "Rotation axis: 'x', 'y', or 'z' (default 'z')." },
                        },
                        ["required"] = new JArray { "object_id", "count", "total_angle" },
                    },
                },
                new JObject
                {
                    ["name"]        = "boolean_union",
                    ["description"] = "Merges two or more overlapping Brep/solid objects into one. Deletes originals on success.",
                    ["input_schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["object_ids"] = new JObject
                            {
                                ["type"]        = "array",
                                ["items"]       = new JObject { ["type"] = "string" },
                                ["description"] = "GUIDs of all objects to union (minimum 2).",
                            },
                        },
                        ["required"] = new JArray { "object_ids" },
                    },
                },
                new JObject
                {
                    ["name"]        = "boolean_difference",
                    ["description"] = "Subtracts cutter objects from target objects. Deletes all originals on success.",
                    ["input_schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["target_ids"] = new JObject
                            {
                                ["type"]        = "array",
                                ["items"]       = new JObject { ["type"] = "string" },
                                ["description"] = "GUIDs of the objects to subtract FROM.",
                            },
                            ["cutter_ids"] = new JObject
                            {
                                ["type"]        = "array",
                                ["items"]       = new JObject { ["type"] = "string" },
                                ["description"] = "GUIDs of the cutting objects.",
                            },
                        },
                        ["required"] = new JArray { "target_ids", "cutter_ids" },
                    },
                },
                new JObject
                {
                    ["name"]        = "boolean_intersection",
                    ["description"] = "Keeps only the overlapping volume between two sets of Brep objects. Deletes originals on success.",
                    ["input_schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["target_ids"] = new JObject
                            {
                                ["type"]        = "array",
                                ["items"]       = new JObject { ["type"] = "string" },
                                ["description"] = "GUIDs of the first set.",
                            },
                            ["cutter_ids"] = new JObject
                            {
                                ["type"]        = "array",
                                ["items"]       = new JObject { ["type"] = "string" },
                                ["description"] = "GUIDs of the second set.",
                            },
                        },
                        ["required"] = new JArray { "target_ids", "cutter_ids" },
                    },
                },
                new JObject
                {
                    ["name"]        = "join_curves",
                    ["description"] = "Joins two or more open curves into a single curve or polycurve. Deletes originals on success.",
                    ["input_schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["object_ids"] = new JObject
                            {
                                ["type"]        = "array",
                                ["items"]       = new JObject { ["type"] = "string" },
                                ["description"] = "GUIDs of the curves to join (minimum 2).",
                            },
                        },
                        ["required"] = new JArray { "object_ids" },
                    },
                },
                new JObject
                {
                    ["name"]        = "set_object_layer",
                    ["description"] = "Moves a Rhino object to a different layer.",
                    ["input_schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["object_id"]  = new JObject { ["type"] = "string", ["description"] = "GUID of the object." },
                            ["layer_name"] = new JObject { ["type"] = "string", ["description"] = "Name of the target layer. Creates it if it does not exist." },
                        },
                        ["required"] = new JArray { "object_id", "layer_name" },
                    },
                },
                new JObject
                {
                    ["name"]        = "set_object_color",
                    ["description"] = "Sets the display colour of a Rhino object using RGB values (0–255 each).",
                    ["input_schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["object_id"] = new JObject { ["type"] = "string", ["description"] = "GUID of the object." },
                            ["r"]         = new JObject { ["type"] = "integer", ["description"] = "Red channel (0–255)." },
                            ["g"]         = new JObject { ["type"] = "integer", ["description"] = "Green channel (0–255)." },
                            ["b"]         = new JObject { ["type"] = "integer", ["description"] = "Blue channel (0–255)." },
                        },
                        ["required"] = new JArray { "object_id", "r", "g", "b" },
                    },
                },
                new JObject
                {
                    ["name"]        = "capture_and_assess",
                    ["description"] = "Captures the active Rhino viewport and injects it as a vision image into the next AI turn. " +
                                      "Call this after completing a modeling step to visually verify the result before proceeding. " +
                                      "Returns the file path of the captured image; the image itself is sent to the model automatically.",
                    ["input_schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["prompt"] = new JObject { ["type"] = "string", ["description"] = "Optional: what to look for in the image (e.g. 'verify the boolean union succeeded')." },
                        },
                        ["required"] = new JArray(),
                    },
                },
                new JObject
                {
                    ["name"]        = "build_gh_definition",
                    ["description"] =
                        "Programmatically builds a Grasshopper definition by creating components and wiring them together. " +
                        "Supported types: 'slider' (number slider), 'panel' (text/value panel), 'toggle' (boolean toggle), " +
                        "'component' (GH component by name — fuzzy matched, e.g. 'Sphere' matches 'Sphere SRF', " +
                        "'Box' matches 'Box 2Pt', 'Pt' or 'Construct Point' for points, 'Loft', 'Circle', 'Extrude'), " +
                        "'python3' (Python 3 script component with code/inputs/outputs), 'sdk' (component by GUID). " +
                        "Wires: 'id:paramIndex' (0-based). Sliders/toggles output index is always 0. " +
                        "PREFER python3 type for geometry creation — more reliable than component type. " +
                        "Set solve:true to compute the solution after building.",
                    ["input_schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["components"] = new JObject
                            {
                                ["type"]        = "array",
                                ["description"] = "Components to create on the canvas.",
                                ["items"]       = new JObject
                                {
                                    ["type"] = "object",
                                    ["properties"] = new JObject
                                    {
                                        ["id"]             = new JObject { ["type"] = "string",  ["description"] = "Local reference ID used in wires." },
                                        ["type"]           = new JObject { ["type"] = "string",  ["description"] = "'slider', 'panel', 'toggle', 'component', 'python3', or 'sdk'." },
                                        ["name"]           = new JObject { ["type"] = "string",  ["description"] = "Display name / NickName." },
                                        ["value"]          = new JObject { ["type"] = "number",  ["description"] = "Initial value (slider only)." },
                                        ["min"]            = new JObject { ["type"] = "number",  ["description"] = "Minimum value (slider only, default 0)." },
                                        ["max"]            = new JObject { ["type"] = "number",  ["description"] = "Maximum value (slider only, default 100)." },
                                        ["text"]           = new JObject { ["type"] = "string",  ["description"] = "Initial text (panel only)." },
                                        ["checked"]        = new JObject { ["type"] = "boolean", ["description"] = "Initial state (toggle only)." },
                                        ["component_name"] = new JObject { ["type"] = "string",  ["description"] = "GH component name to instantiate (component type only), e.g. 'Box', 'Loft', 'Area'." },
                                        ["code"]           = new JObject { ["type"] = "string",  ["description"] = "Python 3 source code (python3 type only)." },
                                        ["guid"]           = new JObject { ["type"] = "string",  ["description"] = "Component GUID for SDK lookup (sdk type only)." },
                                        ["inputs"]         = new JObject { ["type"] = "array",   ["description"] = "Input parameter names (python3 type only).", ["items"] = new JObject { ["type"] = "string" } },
                                        ["outputs"]        = new JObject { ["type"] = "array",   ["description"] = "Output parameter names (python3 type only).", ["items"] = new JObject { ["type"] = "string" } },
                                    },
                                    ["required"] = new JArray { "id", "type" },
                                },
                            },
                            ["wires"] = new JObject
                            {
                                ["type"]        = "array",
                                ["description"] = "Connections between component outputs and inputs. Format: 'compId:paramIndex' (0-based).",
                                ["items"]       = new JObject
                                {
                                    ["type"] = "object",
                                    ["properties"] = new JObject
                                    {
                                        ["from"] = new JObject { ["type"] = "string", ["description"] = "Source: 'compId:outputIndex'." },
                                        ["to"]   = new JObject { ["type"] = "string", ["description"] = "Target: 'compId:inputIndex'." },
                                    },
                                },
                            },
                            ["solve"]        = new JObject { ["type"] = "boolean", ["description"] = "Trigger a GH solution after building (default true)." },
                            ["clear_canvas"] = new JObject { ["type"] = "boolean", ["description"] = "Clear existing canvas objects before building (default false)." },
                        },
                        ["required"] = new JArray { "components" },
                    },
                },
                new JObject
                {
                    ["name"]        = "solve_gh_definition",
                    ["description"] = "Triggers a new solution on the active Grasshopper canvas, recomputing all outputs.",
                    ["input_schema"] = new JObject { ["type"] = "object", ["properties"] = new JObject(), ["required"] = new JArray() },
                },
                new JObject
                {
                    ["name"]        = "bake_gh_definition",
                    ["description"] = "Bakes all geometry outputs from the active Grasshopper canvas to a named Rhino layer.",
                    ["input_schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["layer_name"] = new JObject { ["type"] = "string",  ["description"] = "Target layer name for baked objects (created if absent)." },
                            ["color_r"]    = new JObject { ["type"] = "integer", ["description"] = "Optional red channel (0-255) for baked objects." },
                            ["color_g"]    = new JObject { ["type"] = "integer", ["description"] = "Optional green channel (0-255)." },
                            ["color_b"]    = new JObject { ["type"] = "integer", ["description"] = "Optional blue channel (0-255)." },
                        },
                        ["required"] = new JArray { "layer_name" },
                    },
                },
            };
        }

        // Replaceable in tests. Default: call Execute normally.
        internal static Func<string, JObject, string> OverrideDispatcher = null;

        // ── Dispatch ─────────────────────────────────────────────────────────────

        public static string Execute(string name, JObject input, RhinoDoc _unused)
        {
            try
            {
                return ExecuteInternal(name, input);
            }
            catch (Exception ex)
            {
                return new JObject
                {
                    ["success"]    = false,
                    ["error"]      = "exception",
                    ["tool"]       = name,
                    ["message"]    = ex.Message,
                    ["suggestion"] = "Check parameters and try again.",
                }.ToString(Formatting.None);
            }
        }

        private static string ExecuteInternal(string name, JObject input)
        {
            switch (name)
            {
                case "get_selected_objects":    return GetSelectedObjects();
                case "select_objects_by_id":    return SelectObjectsById(input["ids"] as JArray);
                case "get_object_info":      return GetObjectInfo(S(input, "object_id"));
                case "get_volume":           return GetVolume(S(input, "object_id"));
                case "move_object":          return MoveObject(S(input, "object_id"),
                                                 D(input, "x"), D(input, "y"), D(input, "z"));
                case "scale_object":         return ScaleObject(S(input, "object_id"), D(input, "factor"),
                                                 D(input, "base_x"), D(input, "base_y"), D(input, "base_z"));
                case "list_layers":          return ListLayers();
case "list_gh_sliders":      return ListGhSliders();
                case "list_gh_components":   return ListGhComponents();
                case "search_gh_components": return SearchGhComponents(S(input, "keyword"));
                case "set_gh_slider":        return SetGhSlider(S(input, "name"), D(input, "value"));
                case "capture_viewport":     return CaptureViewport();
                case "run_rhino_command":    return RunRhinoCommand(S(input, "command"), input["echo"]?.ToObject<bool>() ?? true);
                case "get_document_summary": return GetDocumentSummary();
                case "delete_object":        return DeleteObject(S(input, "object_id"));
                case "rename_object":        return RenameObject(S(input, "object_id"), S(input, "name"));
                case "create_layer":         return CreateLayer(S(input, "name"));
                case "set_current_layer":    return SetCurrentLayer(S(input, "name"));
                case "undo":                 return UndoRedo("_Undo", input["steps"]?.ToObject<int>() ?? 1);
                case "redo":                 return UndoRedo("_Redo", input["steps"]?.ToObject<int>() ?? 1);
                case "execute_python_code":   return ExecutePythonCode(S(input, "code"));
                case "rotate_object":         return RotateObject(S(input, "object_id"), D(input, "angle_degrees"),
                                                  S(input, "axis"), D(input, "center_x"), D(input, "center_y"), D(input, "center_z"));
                case "mirror_object":         return MirrorObject(S(input, "object_id"), S(input, "mirror_plane"), D(input, "plane_offset"));
                case "array_linear":          return ArrayLinear(S(input, "object_id"), D(input, "dx"), D(input, "dy"), D(input, "dz"),
                                                  input["count"]?.ToObject<int>() ?? 1);
                case "array_polar":           return ArrayPolar(S(input, "object_id"),
                                                  input["count"]?.ToObject<int>() ?? 1, D(input, "total_angle"),
                                                  D(input, "center_x"), D(input, "center_y"), D(input, "center_z"), S(input, "axis"));
                case "boolean_union":         return BooleanUnion(input["object_ids"] as JArray);
                case "boolean_difference":    return BooleanDifference(input["target_ids"] as JArray, input["cutter_ids"] as JArray);
                case "boolean_intersection":  return BooleanIntersection(input["target_ids"] as JArray, input["cutter_ids"] as JArray);
                case "join_curves":           return JoinCurves(input["object_ids"] as JArray);
                case "set_object_layer":      return SetObjectLayer(S(input, "object_id"), S(input, "layer_name"));
                case "set_object_color":      return SetObjectColor(S(input, "object_id"),
                                                  input["r"]?.ToObject<int>() ?? 0, input["g"]?.ToObject<int>() ?? 0, input["b"]?.ToObject<int>() ?? 0);
                case "build_gh_definition":   return BuildGhDefinition(
                                                  input["components"] as JArray, input["wires"] as JArray,
                                                  input["solve"]?.ToObject<bool>() ?? true,
                                                  input["clear_canvas"]?.ToObject<bool>() ?? false);
                case "capture_and_assess":    return CaptureAndAssess(S(input, "prompt"));
                case "get_scene_layout":      return GetSceneLayout();
                case "place_object":          return PlaceObject(S(input, "object_id"), S(input, "anchor"),
                                                  input["target_x"], input["target_y"], input["target_z"]);
                case "solve_gh_definition":   return SolveGhDefinition();
                case "bake_gh_definition":    return BakeGhDefinition(
                                                  S(input, "layer_name"),
                                                  input["color_r"]?.ToObject<int>(),
                                                  input["color_g"]?.ToObject<int>(),
                                                  input["color_b"]?.ToObject<int>());
                default:
                    // Dynamic tools from the registry (rhino_cmd_* and gh_comp_*)
                    if (name.StartsWith("rhino_cmd_") || name.StartsWith("gh_comp_"))
                        return RhinoCommandRegistry.ExecuteDynamic(name, input);
                    return Fail($"Unknown tool: {name}");
            }
        }

        // ── Tool implementations ─────────────────────────────────────────────────

        private static string GetSelectedObjects()
        {
            return OnMain(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return new JObject { ["success"] = false, ["message"] = "No active Rhino document." };

                var selected = doc.Objects.GetSelectedObjects(false, false).ToList();
                if (selected.Count == 0)
                    return Obj("count", 0, "objects", new JArray(), "message", "No objects selected.");

                var arr = new JArray();
                foreach (var obj in selected)
                    arr.Add(new JObject
                    {
                        ["id"]    = obj.Id.ToString(),
                        ["type"]  = obj.Geometry?.GetType().Name ?? "unknown",
                        ["layer"] = doc.Layers[obj.Attributes.LayerIndex].Name,
                        ["name"]  = obj.Attributes.Name ?? "",
                    });
                return Obj("count", selected.Count, "objects", arr);
            });
        }

        private static string SelectObjectsById(JArray ids)
        {
            if (ids == null || ids.Count == 0) return Fail("ids array is required and must not be empty.");
            return OnMain(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return new JObject { ["success"] = false, ["message"] = "No active Rhino document." };

                doc.Objects.UnselectAll();

                var selected = new JArray();
                var missing  = new JArray();
                foreach (var token in ids)
                {
                    var idStr = token?.ToString();
                    if (!Guid.TryParse(idStr, out var guid)) { missing.Add(idStr); continue; }
                    var obj = doc.Objects.Find(guid);
                    if (obj == null) { missing.Add(idStr); continue; }
                    obj.Select(true);
                    selected.Add(new JObject
                    {
                        ["id"]    = idStr,
                        ["type"]  = obj.Geometry?.GetType().Name ?? "unknown",
                        ["layer"] = doc.Layers[obj.Attributes.LayerIndex].Name,
                    });
                }
                doc.Views.Redraw();

                var result = Obj("selected_count", selected.Count, "objects", selected);
                if (missing.Count > 0) result["not_found"] = missing;
                return result;
            });
        }

        private static string GetObjectInfo(string objectId)
        {
            if (string.IsNullOrEmpty(objectId)) return Fail("object_id is required.");
            return OnMain(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return new JObject { ["success"] = false, ["message"] = "No active Rhino document." };
                if (!Guid.TryParse(objectId, out var guid)) return new JObject { ["success"] = false, ["message"] = "Invalid GUID." };
                var obj = doc.Objects.Find(guid);
                if (obj == null) return new JObject { ["success"] = false, ["message"] = "Object not found." };

                var geom = obj.Geometry;
                var mp   = ComputeVolume(geom);
                var ap   = ComputeArea(geom);
                var bb   = geom.GetBoundingBox(true);

                return Obj(
                    "id",           objectId,
                    "type",         geom.GetType().Name,
                    "layer",        doc.Layers[obj.Attributes.LayerIndex].Name,
                    "name",         obj.Attributes.Name ?? "",
                    "volume",       Math.Round(mp?.Volume ?? 0, 4),
                    "area",         Math.Round(ap?.Area   ?? 0, 4),
                    "bounding_box", new JArray { bb.Min.X, bb.Min.Y, bb.Min.Z, bb.Max.X, bb.Max.Y, bb.Max.Z }
                );
            });
        }

        private static string GetSceneLayout()
        {
            return OnMain(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return new JObject { ["success"] = false, ["message"] = "No active document." };

                var objects = new JArray();
                foreach (var obj in doc.Objects.GetObjectList(Rhino.DocObjects.ObjectType.AnyObject))
                {
                    if (obj.IsDeleted || obj.Geometry == null) continue;
                    var bb  = obj.Geometry.GetBoundingBox(true);
                    if (!bb.IsValid) continue;
                    var ctr = bb.Center;
                    objects.Add(new JObject
                    {
                        ["id"]     = obj.Id.ToString(),
                        ["type"]   = obj.Geometry.GetType().Name,
                        ["name"]   = obj.Attributes.Name ?? "",
                        ["layer"]  = doc.Layers[obj.Attributes.LayerIndex].Name,
                        ["bbox"]   = new JArray {
                            Math.Round(bb.Min.X, 3), Math.Round(bb.Min.Y, 3), Math.Round(bb.Min.Z, 3),
                            Math.Round(bb.Max.X, 3), Math.Round(bb.Max.Y, 3), Math.Round(bb.Max.Z, 3),
                        },
                        ["center"] = new JArray {
                            Math.Round(ctr.X, 3), Math.Round(ctr.Y, 3), Math.Round(ctr.Z, 3),
                        },
                    });
                }
                return new JObject
                {
                    ["success"] = true,
                    ["count"]   = objects.Count,
                    ["objects"] = objects,
                };
            });
        }

        private static string PlaceObject(string objectId, string anchor, JToken tx, JToken ty, JToken tz)
        {
            if (string.IsNullOrEmpty(objectId)) return Fail("object_id is required.");
            return OnMain(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return new JObject { ["success"] = false, ["message"] = "No active document." };
                if (!Guid.TryParse(objectId, out var guid))
                    return new JObject { ["success"] = false, ["message"] = "Invalid GUID." };
                var obj = doc.Objects.Find(guid);
                if (obj == null) return new JObject { ["success"] = false, ["message"] = "Object not found." };

                var bb = obj.Geometry.GetBoundingBox(true);
                if (!bb.IsValid) return new JObject { ["success"] = false, ["message"] = "Bounding box invalid." };

                // Determine the current anchor point
                Point3d anchorPt;
                switch ((anchor ?? "base").ToLower())
                {
                    case "center": anchorPt = bb.Center; break;
                    case "min":    anchorPt = bb.Min;    break;
                    default: // "base" — bottom center
                        anchorPt = new Point3d((bb.Min.X + bb.Max.X) / 2.0,
                                               (bb.Min.Y + bb.Max.Y) / 2.0,
                                               bb.Min.Z);
                        break;
                }

                double dx = tx != null ? (tx.ToObject<double>() - anchorPt.X) : 0.0;
                double dy = ty != null ? (ty.ToObject<double>() - anchorPt.Y) : 0.0;
                double dz = tz != null ? (tz.ToObject<double>() - anchorPt.Z) : 0.0;

                if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9 && Math.Abs(dz) < 1e-9)
                    return new JObject { ["success"] = true, ["message"] = "Object already at target position." };

                uint sn   = doc.BeginUndoRecord("PenguinClaw: place_object");
                var  xform = Transform.Translation(dx, dy, dz);
                var  newId = doc.Objects.Transform(guid, xform, true);
                doc.EndUndoRecord(sn);
                doc.Views.Redraw();

                if (newId == Guid.Empty)
                    return new JObject { ["success"] = false, ["message"] = "Transform failed." };

                return new JObject
                {
                    ["success"] = true,
                    ["message"] = $"Placed {objectId} (anchor={anchor ?? "base"}) at ({tx},{ty},{tz}), moved by ({Math.Round(dx,3)},{Math.Round(dy,3)},{Math.Round(dz,3)}).",
                    ["new_id"]  = newId.ToString(),
                };
            });
        }

        private static string GetVolume(string objectId)
        {
            if (string.IsNullOrEmpty(objectId)) return Fail("object_id is required.");
            return OnMain(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return new JObject { ["success"] = false, ["message"] = "No active Rhino document." };
                if (!Guid.TryParse(objectId, out var guid)) return new JObject { ["success"] = false, ["message"] = "Invalid GUID." };
                var obj = doc.Objects.Find(guid);
                if (obj == null) return new JObject { ["success"] = false, ["message"] = "Object not found." };

                var mp = ComputeVolume(obj.Geometry);
                if (mp != null) return Obj("id", objectId, "volume", Math.Round(mp.Volume, 4));
                return new JObject { ["success"] = false, ["message"] = "Volume could not be computed (open or unsupported geometry)." };
            });
        }

        private static string MoveObject(string objectId, double x, double y, double z)
        {
            if (string.IsNullOrEmpty(objectId)) return Fail("object_id is required.");
            return OnMain(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return new JObject { ["success"] = false, ["message"] = "No active Rhino document." };
                if (!Guid.TryParse(objectId, out var guid)) return new JObject { ["success"] = false, ["message"] = "Invalid GUID." };
                uint undoSn = doc.BeginUndoRecord("PenguinClaw: move_object");
                var xform = Transform.Translation(new Vector3d(x, y, z));
                var newId = doc.Objects.Transform(guid, xform, true);
                doc.EndUndoRecord(undoSn);
                if (newId == Guid.Empty) return new JObject { ["success"] = false, ["message"] = "Transform failed." };
                doc.Views.Redraw();
                return Obj("message", $"Moved {objectId} by ({x}, {y}, {z}).");
            });
        }

        private static string ScaleObject(string objectId, double factor, double bx, double by, double bz)
        {
            if (string.IsNullOrEmpty(objectId)) return Fail("object_id is required.");
            if (factor <= 0) return Fail("factor must be greater than 0.");
            return OnMain(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return new JObject { ["success"] = false, ["message"] = "No active Rhino document." };
                if (!Guid.TryParse(objectId, out var guid)) return new JObject { ["success"] = false, ["message"] = "Invalid GUID." };
                uint undoSn = doc.BeginUndoRecord("PenguinClaw: scale_object");
                var xform = Transform.Scale(new Point3d(bx, by, bz), factor);
                var newId = doc.Objects.Transform(guid, xform, true);
                doc.EndUndoRecord(undoSn);
                if (newId == Guid.Empty) return new JObject { ["success"] = false, ["message"] = "Scale transform failed." };
                doc.Views.Redraw();
                return Obj("message", $"Scaled {objectId} by {factor}x from ({bx},{by},{bz}).", "new_id", newId.ToString());
            });
        }

        private static string ListLayers()
        {
            return OnMain(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return new JObject { ["success"] = false, ["message"] = "No active Rhino document." };
                var arr = new JArray();
                foreach (var layer in doc.Layers)
                    arr.Add(new JObject
                    {
                        ["name"]      = layer.Name,
                        ["full_path"] = layer.FullPath,
                        ["visible"]   = layer.IsVisible,
                        ["locked"]    = layer.IsLocked,
                        ["index"]     = layer.Index,
                    });
                return Obj("count", arr.Count, "layers", arr);
            });
        }


        private static string ListGhSliders()
        {
            return OnMain(() =>
            {
                var ghDoc = GetGhDocument();
                if (ghDoc == null) return new JObject { ["success"] = false, ["message"] = "No active Grasshopper canvas." };

                var sliders = new JArray();
                foreach (var obj in GetGhObjects(ghDoc))
                {
                    if (obj == null) continue;
                    var t  = obj.GetType();
                    var sl = t.GetProperty("Slider", BindingFlags.Public | BindingFlags.Instance)?.GetValue(obj);
                    if (sl == null) continue;
                    var st = sl.GetType();
                    sliders.Add(new JObject
                    {
                        ["name"]  = GProp<string>(obj, t, "NickName") ?? "",
                        ["value"] = Convert.ToDouble(GProp<object>(sl, st, "Value")    ?? 0.0),
                        ["min"]   = Convert.ToDouble(GProp<object>(sl, st, "Minimum")  ?? 0.0),
                        ["max"]   = Convert.ToDouble(GProp<object>(sl, st, "Maximum")  ?? 1.0),
                    });
                }
                return Obj("count", sliders.Count, "sliders", sliders);
            });
        }

        private static string ListGhComponents()
        {
            return OnMain(() =>
            {
                var ghDoc = GetGhDocument();
                if (ghDoc == null) return new JObject { ["success"] = false, ["message"] = "No active Grasshopper canvas." };

                var comps = new JArray();
                foreach (var obj in GetGhObjects(ghDoc))
                {
                    if (obj == null) continue;
                    var t    = obj.GetType();
                    var name = GProp<string>(obj, t, "NickName")
                            ?? GProp<string>(obj, t, "Name")
                            ?? t.Name;
                    comps.Add(new JObject
                    {
                        ["name"]          = name,
                        ["type"]          = t.Name,
                        ["instance_guid"] = GProp<object>(obj, t, "InstanceGuid")?.ToString() ?? "",
                    });
                }
                return Obj("count", comps.Count, "components", comps);
            });
        }

        private static string SearchGhComponents(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return Fail("keyword is required.");
            return OnMain(() =>
            {
                var ghAsm     = Assembly.Load("Grasshopper");
                var instances = ghAsm?.GetType("Grasshopper.Instances");
                var server    = instances?.GetProperty("ComponentServer", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var proxies   = server?.GetType()
                    .GetProperty("ObjectProxies", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(server) as IEnumerable;
                if (proxies == null) return new JObject { ["success"] = false, ["message"] = "Could not access GH component server." };

                var results = new JArray();
                foreach (var p in proxies)
                {
                    if (p == null) continue;
                    var pt   = p.GetType();
                    // Name/Category may be directly on proxy OR under Desc
                    var name = pt.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)?.GetValue(p)?.ToString();
                    var cat  = pt.GetProperty("Category", BindingFlags.Public | BindingFlags.Instance)?.GetValue(p)?.ToString() ?? "";
                    if (name == null)
                    {
                        var desc = pt.GetProperty("Desc", BindingFlags.Public | BindingFlags.Instance)?.GetValue(p);
                        if (desc != null)
                        {
                            var dt = desc.GetType();
                            name = dt.GetProperty("Name",     BindingFlags.Public | BindingFlags.Instance)?.GetValue(desc)?.ToString();
                            if (string.IsNullOrEmpty(cat))
                                cat = dt.GetProperty("Category", BindingFlags.Public | BindingFlags.Instance)?.GetValue(desc)?.ToString() ?? "";
                        }
                    }
                    if (name == null || name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var guid = pt.GetProperty("Guid", BindingFlags.Public | BindingFlags.Instance)?.GetValue(p)?.ToString() ?? "";
                    results.Add(new JObject { ["name"] = name, ["guid"] = guid, ["category"] = cat });
                }
                return Obj("keyword", keyword, "count", results.Count, "matches", results);
            });
        }

        private static string SetGhSlider(string name, double value)
        {
            if (string.IsNullOrEmpty(name)) return Fail("name is required.");
            return OnMain(() =>
            {
                var ghDoc = GetGhDocument();
                if (ghDoc == null) return new JObject { ["success"] = false, ["message"] = "No active Grasshopper canvas." };

                foreach (var obj in GetGhObjects(ghDoc))
                {
                    if (obj == null) continue;
                    var t = obj.GetType();
                    if (!string.Equals(GProp<string>(obj, t, "NickName"), name, StringComparison.OrdinalIgnoreCase)) continue;

                    var slProp = t.GetProperty("Slider", BindingFlags.Public | BindingFlags.Instance);
                    if (slProp == null) continue;
                    var sl = slProp.GetValue(obj);
                    if (sl == null) continue;

                    var st    = sl.GetType();
                    var vProp = st.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
                    if (vProp == null) continue;

                    vProp.SetValue(sl, (decimal)value); // GH sliders store decimal

                    ghDoc.GetType()
                         .GetMethod("NewSolution", BindingFlags.Public | BindingFlags.Instance,
                                    null, new[] { typeof(bool) }, null)
                         ?.Invoke(ghDoc, new object[] { false });

                    return Obj("message", $"Slider '{name}' set to {value}.");
                }
                return new JObject { ["success"] = false, ["message"] = $"Slider '{name}' not found." };
            });
        }

        private static string CaptureViewport()
        {
            return OnMain(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return new JObject { ["success"] = false, ["message"] = "No active Rhino document." };
                var view = doc.Views.ActiveView;
                if (view == null) return new JObject { ["success"] = false, ["message"] = "No active view." };

                var folder = Path.Combine(Path.GetTempPath(), "penguinclaw");
                Directory.CreateDirectory(folder);
                var path     = Path.Combine(folder, "viewport.png");
                var viewRect = view.ClientRectangle;

                // Cap capture resolution to 1280 px on the longest side to limit PNG size
                const int MaxCaptureDim = 1280;
                int capW = viewRect.Width  > 0 ? viewRect.Width  : 1024;
                int capH = viewRect.Height > 0 ? viewRect.Height : 768;
                if (capW > MaxCaptureDim || capH > MaxCaptureDim)
                {
                    double s = Math.Min((double)MaxCaptureDim / capW, (double)MaxCaptureDim / capH);
                    capW = (int)(capW * s);
                    capH = (int)(capH * s);
                }

                var bitmap = view.CaptureToBitmap(new System.Drawing.Size(capW, capH));
                if (bitmap == null) return new JObject { ["success"] = false, ["message"] = "CaptureToBitmap returned null." };

                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                return Obj("path", path);
            });
        }

        // Called by RhinoCommandRegistry for dynamic rhino_cmd_* tools
        internal static string RunCommandInternal(string command) => RunRhinoCommand(command, true);

        private static string RunRhinoCommand(string command, bool echo)
        {
            if (string.IsNullOrWhiteSpace(command)) return Fail("command is required.");
            return OnMain(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return new JObject { ["success"] = false, ["message"] = "No active Rhino document." };

                // Snapshot existing IDs before the command so we can detect new objects reliably
                // (selection state after a command is unreliable — appended _Enter deselects)
                var beforeIds = new HashSet<Guid>(
                    doc.Objects.Where(o => !o.IsDeleted).Select(o => o.Id));

                // Append _Enter if not already present to prevent commands from hanging
                var cmd = command.TrimEnd();
                if (!cmd.EndsWith("_Enter", StringComparison.OrdinalIgnoreCase) &&
                    !cmd.EndsWith(" Enter", StringComparison.OrdinalIgnoreCase) &&
                    !cmd.EndsWith("\n"))
                    cmd = cmd + " _Enter";
                bool ok = RhinoApp.RunScript(cmd, echo);
                doc.Views.Redraw();

                var result = new JObject
                {
                    ["success"] = ok,
                    ["result"]  = ok ? "success" : "failure",
                    ["command"] = command,
                };

                // Prefer newly created objects; fall back to current selection
                var newObjs = doc.Objects
                    .Where(o => !o.IsDeleted && !beforeIds.Contains(o.Id))
                    .ToList();
                var toReport = newObjs.Count > 0
                    ? newObjs
                    : doc.Objects.GetSelectedObjects(false, false).ToList();

                if (toReport.Count > 0)
                {
                    var arr = new JArray();
                    foreach (var obj in toReport)
                        arr.Add(new JObject
                        {
                            ["id"]    = obj.Id.ToString(),
                            ["type"]  = obj.Geometry?.GetType().Name ?? "unknown",
                            ["layer"] = doc.Layers[obj.Attributes.LayerIndex].Name,
                        });
                    result["selected_objects"] = arr;
                }

                return result;
            });
        }

        private static string GetDocumentSummary()
        {
            return OnMain(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return new JObject { ["success"] = false, ["message"] = "No active Rhino document." };

                var allObjs = doc.Objects.Where(o => o.IsValid && !o.IsDeleted).ToList();

                // Count by geometry type
                var byType = new JObject();
                foreach (var grp in allObjs.GroupBy(o => o.Geometry?.GetType().Name ?? "Unknown"))
                    byType[grp.Key] = grp.Count();

                // Count by layer
                var byLayer = new JObject();
                foreach (var grp in allObjs.GroupBy(o => doc.Layers[o.Attributes.LayerIndex].Name))
                    byLayer[grp.Key] = grp.Count();

                // Scene bounding box
                BoundingBox bb = BoundingBox.Empty;
                foreach (var o in allObjs)
                {
                    var geomBb = o.Geometry?.GetBoundingBox(false) ?? BoundingBox.Empty;
                    if (geomBb.IsValid) bb.Union(geomBb);
                }

                var activeLayer = doc.Layers[doc.Layers.CurrentLayerIndex].Name;

                return Obj(
                    "document",      doc.Name ?? "(unsaved)",
                    "total_objects", allObjs.Count,
                    "by_type",       byType,
                    "by_layer",      byLayer,
                    "active_layer",  activeLayer,
                    "bounding_box",  bb.IsValid
                        ? (object)new JArray { bb.Min.X, bb.Min.Y, bb.Min.Z, bb.Max.X, bb.Max.Y, bb.Max.Z }
                        : null
                );
            });
        }

        private static string DeleteObject(string objectId)
        {
            if (string.IsNullOrEmpty(objectId)) return Fail("object_id is required.");
            return OnMain(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return new JObject { ["success"] = false, ["message"] = "No active Rhino document." };
                if (!Guid.TryParse(objectId, out var guid)) return new JObject { ["success"] = false, ["message"] = "Invalid GUID." };
                if (doc.Objects.Find(guid) == null) return new JObject { ["success"] = false, ["message"] = "Object not found." };
                uint undoSn = doc.BeginUndoRecord("PenguinClaw: delete_object");
                bool ok = doc.Objects.Delete(guid, true);
                doc.EndUndoRecord(undoSn);
                if (!ok) return new JObject { ["success"] = false, ["message"] = "Delete failed." };
                doc.Views.Redraw();
                return Obj("message", $"Deleted object {objectId}.");
            });
        }

        private static string RenameObject(string objectId, string name)
        {
            if (string.IsNullOrEmpty(objectId)) return Fail("object_id is required.");
            if (name == null) return Fail("name is required.");
            return OnMain(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return new JObject { ["success"] = false, ["message"] = "No active Rhino document." };
                if (!Guid.TryParse(objectId, out var guid)) return new JObject { ["success"] = false, ["message"] = "Invalid GUID." };
                var obj = doc.Objects.Find(guid);
                if (obj == null) return new JObject { ["success"] = false, ["message"] = "Object not found." };
                uint undoSn = doc.BeginUndoRecord("PenguinClaw: rename_object");
                var attr = obj.Attributes.Duplicate();
                attr.Name = name;
                bool ok = doc.Objects.ModifyAttributes(guid, attr, true);
                doc.EndUndoRecord(undoSn);
                if (!ok) return new JObject { ["success"] = false, ["message"] = "Rename failed." };
                return Obj("message", $"Renamed object {objectId} to '{name}'.");
            });
        }

        private static string CreateLayer(string name)
        {
            if (string.IsNullOrEmpty(name)) return Fail("name is required.");
            return OnMain(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return new JObject { ["success"] = false, ["message"] = "No active Rhino document." };
                if (doc.Layers.FindName(name) != null)
                    return Obj("message", $"Layer '{name}' already exists.");
                var layer = new Rhino.DocObjects.Layer { Name = name };
                int idx = doc.Layers.Add(layer);
                if (idx < 0) return new JObject { ["success"] = false, ["message"] = "Failed to create layer." };
                return Obj("message", $"Created layer '{name}'.", "index", idx);
            });
        }

        private static string SetCurrentLayer(string name)
        {
            if (string.IsNullOrEmpty(name)) return Fail("name is required.");
            return OnMain(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return new JObject { ["success"] = false, ["message"] = "No active Rhino document." };
                var layer = doc.Layers.FindName(name);
                if (layer == null) return new JObject { ["success"] = false, ["message"] = $"Layer '{name}' not found." };
                doc.Layers.SetCurrentLayerIndex(layer.Index, true);
                return Obj("message", $"Active layer set to '{name}'.");
            });
        }

        private static string UndoRedo(string cmd, int steps)
        {
            if (steps < 1) steps = 1;
            return OnMain(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return new JObject { ["success"] = false, ["message"] = "No active Rhino document." };
                for (int i = 0; i < steps; i++)
                    RhinoApp.RunScript(cmd, false);
                doc.Views.Redraw();
                return Obj("message", $"{cmd.TrimStart('_')} x{steps} completed.");
            });
        }

        // GUID of the IronPython 2 plugin — must be loaded before PythonScript.Create()
        private static readonly Guid IronPythonPluginId = new Guid("814d908a-e25c-493d-97e9-ee3861957f49");

        private static string ExecutePythonCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return Fail("code is required.");
            return OnMain(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return new JObject { ["success"] = false, ["message"] = "No active Rhino document." };

                // Ensure IronPython plugin is loaded — Create() returns null without it
                try { Rhino.PlugIns.PlugIn.LoadPlugIn(IronPythonPluginId.ToString(), out _); }
                catch { /* non-fatal — try anyway */ }

                Rhino.Runtime.PythonScript py;
                try { py = Rhino.Runtime.PythonScript.Create(); }
                catch (Exception ex)
                {
                    return new JObject
                    {
                        ["success"] = false,
                        ["message"] = "Python engine unavailable — ensure the IronPython plugin is installed in Rhino 8.",
                        ["detail"]  = ex.Message,
                    };
                }
                if (py == null)
                    return new JObject
                    {
                        ["success"] = false,
                        ["message"] = "Python engine returned null. The IronPython plugin may not be installed or enabled.",
                    };

                // Normalise line endings — CRLF in JSON causes SyntaxError in IronPython
                code = code.Replace("\r\n", "\n").Replace("\r", "\n");

                var output = new StringBuilder();
                // Capture both normal output and errors from the script
                py.Output = s => output.Append(s);
                py.SetVariable("doc", doc);

                bool ok;
                string errorMsg  = null;
                string traceback = null;
                try
                {
                    ok = py.ExecuteScript(code);
                    if (!ok)
                    {
                        // Extract structured error info via reflection (IronPython engine stores it as properties)
                        try
                        {
                            var t  = py.GetType();
                            var em = t.GetProperty("ErrorMessage")?.GetValue(py)?.ToString();
                            var et = t.GetProperty("ErrorType")?.GetValue(py)?.ToString();
                            var el = t.GetProperty("ErrorLineNumber")?.GetValue(py);
                            var tb = t.GetProperty("Traceback")?.GetValue(py)?.ToString();

                            if (!string.IsNullOrEmpty(tb)) traceback = tb;

                            if (!string.IsNullOrEmpty(em) || !string.IsNullOrEmpty(et))
                            {
                                errorMsg = string.IsNullOrEmpty(et) ? em
                                         : string.IsNullOrEmpty(em) ? et
                                         : $"{et}: {em}";
                                if (el != null) errorMsg += $" (line {el})";
                            }
                            else
                            {
                                errorMsg = "Script returned false — check syntax. Note: this engine is IronPython 2. "
                                         + "Avoid f-strings (use .format()), walrus operator :=, and Python 3-only syntax.";
                            }
                        }
                        catch
                        {
                            errorMsg = "Execution failed. Note: engine is IronPython 2 — avoid f-strings, use str.format() instead.";
                        }
                    }
                }
                catch (Exception ex)
                {
                    ok       = false;
                    errorMsg = ex.Message;
                    // Include inner exception which often has the actual Python traceback
                    if (ex.InnerException != null)
                        traceback = ex.InnerException.ToString();
                }

                doc.Views.Redraw();

                var result = new JObject
                {
                    ["success"] = ok,
                    ["output"]  = output.ToString().TrimEnd(),
                };
                if (!string.IsNullOrEmpty(errorMsg))  result["error"]     = errorMsg;
                if (!string.IsNullOrEmpty(traceback)) result["traceback"] = traceback;
                return result;
            });
        }

        private static string RotateObject(string objectId, double angleDeg, string axis, double cx, double cy, double cz)
        {
            if (string.IsNullOrEmpty(objectId)) return Fail("object_id is required.");
            return OnMain(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return new JObject { ["success"] = false, ["message"] = "No active document." };
                if (!Guid.TryParse(objectId, out var guid)) return new JObject { ["success"] = false, ["message"] = "Invalid GUID." };
                var axisVec = (axis ?? "z").ToLower() == "x" ? Vector3d.XAxis
                            : (axis ?? "z").ToLower() == "y" ? Vector3d.YAxis
                            : Vector3d.ZAxis;
                uint sn = doc.BeginUndoRecord("PenguinClaw: rotate_object");
                var xform = Transform.Rotation(angleDeg * Math.PI / 180.0, axisVec, new Point3d(cx, cy, cz));
                var newId = doc.Objects.Transform(guid, xform, true);
                doc.EndUndoRecord(sn);
                if (newId == Guid.Empty) return new JObject { ["success"] = false, ["message"] = "Rotation failed." };
                doc.Views.Redraw();
                return Obj("message", $"Rotated {objectId} by {angleDeg}° around {axis ?? "z"}-axis.");
            });
        }

        private static string MirrorObject(string objectId, string mirrorPlane, double planeOffset)
        {
            if (string.IsNullOrEmpty(objectId)) return Fail("object_id is required.");
            return OnMain(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return new JObject { ["success"] = false, ["message"] = "No active document." };
                if (!Guid.TryParse(objectId, out var guid)) return new JObject { ["success"] = false, ["message"] = "Invalid GUID." };

                Plane plane;
                switch ((mirrorPlane ?? "xy").ToLower())
                {
                    case "xz": plane = new Plane(new Point3d(0, planeOffset, 0), Vector3d.YAxis); break;
                    case "yz": plane = new Plane(new Point3d(planeOffset, 0, 0), Vector3d.XAxis); break;
                    default:   plane = new Plane(new Point3d(0, 0, planeOffset), Vector3d.ZAxis); break; // xy
                }

                uint sn = doc.BeginUndoRecord("PenguinClaw: mirror_object");
                var xform = Transform.Mirror(plane);
                var obj = doc.Objects.Find(guid);
                if (obj == null) { doc.EndUndoRecord(sn); return new JObject { ["success"] = false, ["message"] = "Object not found." }; }
                var mirrorId = doc.Objects.Transform(guid, xform, false); // false = keep original
                doc.EndUndoRecord(sn);
                if (mirrorId == Guid.Empty) return new JObject { ["success"] = false, ["message"] = "Mirror failed." };
                doc.Objects.Find(mirrorId)?.Select(true);
                doc.Views.Redraw();
                return Obj("message", $"Mirrored {objectId} across {mirrorPlane ?? "xy"} plane.", "new_id", mirrorId.ToString(),
                           "selected_objects", SelectedObjectsJson(doc));
            });
        }

        private static string ArrayLinear(string objectId, double dx, double dy, double dz, int count)
        {
            if (string.IsNullOrEmpty(objectId)) return Fail("object_id is required.");
            if (count < 1) return Fail("count must be at least 1.");
            return OnMain(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return new JObject { ["success"] = false, ["message"] = "No active document." };
                if (!Guid.TryParse(objectId, out var guid)) return new JObject { ["success"] = false, ["message"] = "Invalid GUID." };
                var obj = doc.Objects.Find(guid);
                if (obj == null) return new JObject { ["success"] = false, ["message"] = "Object not found." };

                uint sn = doc.BeginUndoRecord("PenguinClaw: array_linear");
                var step = new Vector3d(dx, dy, dz);
                var newIds = new JArray();
                for (int i = 1; i <= count; i++)
                {
                    var xform = Transform.Translation(step * i);
                    var newId = doc.Objects.Transform(guid, xform, false);
                    if (newId != Guid.Empty) { newIds.Add(newId.ToString()); doc.Objects.Find(newId)?.Select(true); }
                }
                doc.EndUndoRecord(sn);
                doc.Views.Redraw();
                return Obj("message", $"Linear array: {count} copies of {objectId}.", "new_ids", newIds,
                           "selected_objects", SelectedObjectsJson(doc));
            });
        }

        private static string ArrayPolar(string objectId, int count, double totalAngleDeg, double cx, double cy, double cz, string axis)
        {
            if (string.IsNullOrEmpty(objectId)) return Fail("object_id is required.");
            if (count < 1) return Fail("count must be at least 1.");
            return OnMain(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return new JObject { ["success"] = false, ["message"] = "No active document." };
                if (!Guid.TryParse(objectId, out var guid)) return new JObject { ["success"] = false, ["message"] = "Invalid GUID." };
                if (doc.Objects.Find(guid) == null) return new JObject { ["success"] = false, ["message"] = "Object not found." };

                var center  = new Point3d(cx, cy, cz);
                var axisVec = (axis ?? "z").ToLower() == "x" ? Vector3d.XAxis
                            : (axis ?? "z").ToLower() == "y" ? Vector3d.YAxis
                            : Vector3d.ZAxis;
                double stepRad = totalAngleDeg * Math.PI / 180.0 / (count + 1);

                uint sn = doc.BeginUndoRecord("PenguinClaw: array_polar");
                var newIds = new JArray();
                for (int i = 1; i <= count; i++)
                {
                    var xform = Transform.Rotation(stepRad * i, axisVec, center);
                    var newId = doc.Objects.Transform(guid, xform, false);
                    if (newId != Guid.Empty) { newIds.Add(newId.ToString()); doc.Objects.Find(newId)?.Select(true); }
                }
                doc.EndUndoRecord(sn);
                doc.Views.Redraw();
                return Obj("message", $"Polar array: {count} copies over {totalAngleDeg}°.", "new_ids", newIds,
                           "selected_objects", SelectedObjectsJson(doc));
            });
        }

        private static string BooleanUnion(JArray ids)
        {
            if (ids == null || ids.Count < 2) return Fail("At least 2 object IDs required in object_ids.");
            return OnMain(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return new JObject { ["success"] = false, ["message"] = "No active document." };
                var breps = new List<Brep>();
                var guids = new List<Guid>();
                foreach (var token in ids)
                {
                    if (!Guid.TryParse(token.ToString(), out var g)) return new JObject { ["success"] = false, ["message"] = $"Invalid GUID: {token}" };
                    var obj = doc.Objects.Find(g);
                    if (obj == null) return new JObject { ["success"] = false, ["message"] = $"Object not found: {g}" };
                    if (!(obj.Geometry is Brep b)) return new JObject { ["success"] = false, ["message"] = $"Object {g} is not a Brep/solid." };
                    breps.Add(b); guids.Add(g);
                }
                var results = Brep.CreateBooleanUnion(breps, doc.ModelAbsoluteTolerance);
                if (results == null || results.Length == 0)
                    return new JObject { ["success"] = false, ["message"] = "Boolean union failed. Ensure objects overlap." };
                uint sn = doc.BeginUndoRecord("PenguinClaw: boolean_union");
                foreach (var g in guids) doc.Objects.Delete(g, true);
                var newIds = new JArray();
                foreach (var r in results)
                {
                    var newId = doc.Objects.AddBrep(r);
                    if (newId != Guid.Empty) { newIds.Add(newId.ToString()); doc.Objects.Find(newId)?.Select(true); }
                }
                doc.EndUndoRecord(sn);
                doc.Views.Redraw();
                return Obj("message", $"Boolean union: {guids.Count} objects → {results.Length} result(s).", "new_ids", newIds,
                           "selected_objects", SelectedObjectsJson(doc));
            });
        }

        private static string BooleanDifference(JArray targetIds, JArray cutterIds)
        {
            if (targetIds == null || targetIds.Count == 0) return Fail("target_ids is required.");
            if (cutterIds == null || cutterIds.Count == 0) return Fail("cutter_ids is required.");
            return OnMain(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return new JObject { ["success"] = false, ["message"] = "No active document." };
                if (!CollectBreps(doc, targetIds, out var targets, out var tGuids, out var err)) return new JObject { ["success"] = false, ["message"] = err };
                if (!CollectBreps(doc, cutterIds, out var cutters, out var cGuids, out err))   return new JObject { ["success"] = false, ["message"] = err };
                var results = Brep.CreateBooleanDifference(targets, cutters, doc.ModelAbsoluteTolerance);
                if (results == null || results.Length == 0)
                    return new JObject { ["success"] = false, ["message"] = "Boolean difference failed. Ensure objects overlap." };
                uint sn = doc.BeginUndoRecord("PenguinClaw: boolean_difference");
                foreach (var g in tGuids.Concat(cGuids)) doc.Objects.Delete(g, true);
                var newIds = new JArray();
                foreach (var r in results)
                {
                    var newId = doc.Objects.AddBrep(r);
                    if (newId != Guid.Empty) { newIds.Add(newId.ToString()); doc.Objects.Find(newId)?.Select(true); }
                }
                doc.EndUndoRecord(sn);
                doc.Views.Redraw();
                return Obj("message", $"Boolean difference: {results.Length} result(s).", "new_ids", newIds,
                           "selected_objects", SelectedObjectsJson(doc));
            });
        }

        private static string BooleanIntersection(JArray targetIds, JArray cutterIds)
        {
            if (targetIds == null || targetIds.Count == 0) return Fail("target_ids is required.");
            if (cutterIds == null || cutterIds.Count == 0) return Fail("cutter_ids is required.");
            return OnMain(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return new JObject { ["success"] = false, ["message"] = "No active document." };
                if (!CollectBreps(doc, targetIds, out var targets, out var tGuids, out var err)) return new JObject { ["success"] = false, ["message"] = err };
                if (!CollectBreps(doc, cutterIds, out var cutters, out var cGuids, out err))   return new JObject { ["success"] = false, ["message"] = err };
                var results = Brep.CreateBooleanIntersection(targets, cutters, doc.ModelAbsoluteTolerance);
                if (results == null || results.Length == 0)
                    return new JObject { ["success"] = false, ["message"] = "Boolean intersection failed. Ensure objects overlap." };
                uint sn = doc.BeginUndoRecord("PenguinClaw: boolean_intersection");
                foreach (var g in tGuids.Concat(cGuids)) doc.Objects.Delete(g, true);
                var newIds = new JArray();
                foreach (var r in results)
                {
                    var newId = doc.Objects.AddBrep(r);
                    if (newId != Guid.Empty) { newIds.Add(newId.ToString()); doc.Objects.Find(newId)?.Select(true); }
                }
                doc.EndUndoRecord(sn);
                doc.Views.Redraw();
                return Obj("message", $"Boolean intersection: {results.Length} result(s).", "new_ids", newIds,
                           "selected_objects", SelectedObjectsJson(doc));
            });
        }

        private static string JoinCurves(JArray ids)
        {
            if (ids == null || ids.Count < 2) return Fail("At least 2 curve IDs required in object_ids.");
            return OnMain(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return new JObject { ["success"] = false, ["message"] = "No active document." };
                var curves = new List<Curve>();
                var guids  = new List<Guid>();
                foreach (var token in ids)
                {
                    if (!Guid.TryParse(token.ToString(), out var g)) return new JObject { ["success"] = false, ["message"] = $"Invalid GUID: {token}" };
                    var obj = doc.Objects.Find(g);
                    if (obj == null) return new JObject { ["success"] = false, ["message"] = $"Object not found: {g}" };
                    if (!(obj.Geometry is Curve c)) return new JObject { ["success"] = false, ["message"] = $"Object {g} is not a curve." };
                    curves.Add(c); guids.Add(g);
                }
                var results = Curve.JoinCurves(curves, doc.ModelAbsoluteTolerance);
                if (results == null || results.Length == 0)
                    return new JObject { ["success"] = false, ["message"] = "Join failed. Ensure curve endpoints are within tolerance." };
                uint sn = doc.BeginUndoRecord("PenguinClaw: join_curves");
                foreach (var g in guids) doc.Objects.Delete(g, true);
                var newIds = new JArray();
                foreach (var r in results)
                {
                    var newId = doc.Objects.AddCurve(r);
                    if (newId != Guid.Empty) { newIds.Add(newId.ToString()); doc.Objects.Find(newId)?.Select(true); }
                }
                doc.EndUndoRecord(sn);
                doc.Views.Redraw();
                return Obj("message", $"Joined {guids.Count} curves → {results.Length} result(s).", "new_ids", newIds,
                           "selected_objects", SelectedObjectsJson(doc));
            });
        }

        private static string SetObjectLayer(string objectId, string layerName)
        {
            if (string.IsNullOrEmpty(objectId))  return Fail("object_id is required.");
            if (string.IsNullOrEmpty(layerName)) return Fail("layer_name is required.");
            return OnMain(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return new JObject { ["success"] = false, ["message"] = "No active document." };
                if (!Guid.TryParse(objectId, out var guid)) return new JObject { ["success"] = false, ["message"] = "Invalid GUID." };
                var obj = doc.Objects.Find(guid);
                if (obj == null) return new JObject { ["success"] = false, ["message"] = "Object not found." };

                // Create layer if needed
                var layer = doc.Layers.FindName(layerName);
                if (layer == null)
                {
                    var newLayer = new Rhino.DocObjects.Layer { Name = layerName };
                    int idx = doc.Layers.Add(newLayer);
                    if (idx < 0) return new JObject { ["success"] = false, ["message"] = $"Could not create layer '{layerName}'." };
                    layer = doc.Layers[idx];
                }

                uint sn = doc.BeginUndoRecord("PenguinClaw: set_object_layer");
                var attr = obj.Attributes.Duplicate();
                attr.LayerIndex = layer.Index;
                bool ok = doc.Objects.ModifyAttributes(guid, attr, true);
                doc.EndUndoRecord(sn);
                if (!ok) return new JObject { ["success"] = false, ["message"] = "Failed to change layer." };
                doc.Views.Redraw();
                return Obj("message", $"Moved {objectId} to layer '{layerName}'.");
            });
        }

        private static string SetObjectColor(string objectId, int r, int g, int b)
        {
            if (string.IsNullOrEmpty(objectId)) return Fail("object_id is required.");
            r = Math.Max(0, Math.Min(255, r));
            g = Math.Max(0, Math.Min(255, g));
            b = Math.Max(0, Math.Min(255, b));
            return OnMain(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return new JObject { ["success"] = false, ["message"] = "No active document." };
                if (!Guid.TryParse(objectId, out var guid)) return new JObject { ["success"] = false, ["message"] = "Invalid GUID." };
                var obj = doc.Objects.Find(guid);
                if (obj == null) return new JObject { ["success"] = false, ["message"] = "Object not found." };
                uint sn = doc.BeginUndoRecord("PenguinClaw: set_object_color");
                var attr = obj.Attributes.Duplicate();
                attr.ObjectColor = System.Drawing.Color.FromArgb(r, g, b);
                attr.ColorSource = Rhino.DocObjects.ObjectColorSource.ColorFromObject;
                bool ok = doc.Objects.ModifyAttributes(guid, attr, true);
                doc.EndUndoRecord(sn);
                if (!ok) return new JObject { ["success"] = false, ["message"] = "Failed to set colour." };
                doc.Views.Redraw();
                return Obj("message", $"Set colour of {objectId} to rgb({r},{g},{b}).");
            });
        }

        private static string BuildGhDefinition(JArray components, JArray wires, bool solve, bool clearCanvas)
        {
            if (components == null || components.Count == 0) return Fail("components array is required.");
            return OnMain(() =>
            {
                var ghAsm     = Assembly.Load("Grasshopper");
                var instances = ghAsm?.GetType("Grasshopper.Instances");
                var canvas    = instances?.GetProperty("ActiveCanvas", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var ghDoc     = canvas?.GetType().GetProperty("Document", BindingFlags.Public | BindingFlags.Instance)?.GetValue(canvas);
                if (ghDoc == null) return new JObject { ["success"] = false, ["message"] = "No active Grasshopper canvas. Open Grasshopper first." };

                if (clearCanvas)
                    ghDoc.GetType().GetMethod("Clear")?.Invoke(ghDoc, null);

                var compMap = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                var created = new JArray();
                int xPos = 100;

                // ── Create components ────────────────────────────────────────────
                foreach (var def in components)
                {
                    var id       = def["id"]?.ToString();
                    var type     = (def["type"]?.ToString() ?? "component").ToLower();
                    var dispName = def["name"]?.ToString() ?? type;

                    object comp = null;
                    switch (type)
                    {
                        case "slider":
                            comp = GhMakeSlider(ghAsm, dispName,
                                def["value"]?.ToObject<double>() ?? 0,
                                def["min"]?.ToObject<double>()   ?? 0,
                                def["max"]?.ToObject<double>()   ?? 100);
                            break;
                        case "panel":
                            comp = GhMakePanel(ghAsm, dispName, def["text"]?.ToString() ?? "");
                            break;
                        case "toggle":
                            comp = GhMakeToggle(ghAsm, dispName, def["checked"]?.ToObject<bool>() ?? false);
                            break;
                        case "python3":
                            comp = GhMakePython3(ghAsm, dispName,
                                def["code"]?.ToString() ?? "# python3\n",
                                def["inputs"] as JArray,
                                def["outputs"] as JArray);
                            break;
                        case "sdk":
                            var guidStr = def["guid"]?.ToString();
                            if (!string.IsNullOrEmpty(guidStr))
                                comp = GhMakeComponentByGuid(ghAsm, guidStr);
                            else
                                comp = GhMakeComponent(ghAsm, def["component_name"]?.ToString() ?? dispName);
                            break;
                        default: // "component"
                            var cname = def["component_name"]?.ToString() ?? dispName;
                            comp = GhMakeComponent(ghAsm, cname);
                            break;
                    }

                    if (comp == null)
                    {
                        var tried = type == "python3" ? "python3 (tried: Python 3 Script, Python3 Script, Python Script, GH_ScriptComponent)"
                                  : type == "component" ? $"component '{def["component_name"] ?? def["name"]}' (not found in GH server — use search_gh_components to find exact name or GUID)"
                                  : $"{type} '{def["component_name"] ?? def["name"]}'";
                        created.Add(new JObject { ["id"] = id, ["status"] = "failed", ["reason"] = $"Could not create {tried}" });
                        continue;
                    }

                    // Set NickName
                    try { comp.GetType().GetProperty("NickName", BindingFlags.Public | BindingFlags.Instance)?.SetValue(comp, dispName); } catch { }

                    // Position on canvas
                    GhSetPivot(comp, xPos, 200);
                    xPos += 230;

                    // Add to GH document
                    var addMethod = ghDoc.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "AddObject" && m.GetParameters().Length == 2);
                    addMethod?.Invoke(ghDoc, new[] { comp, (object)false });

                    if (id != null) compMap[id] = comp;
                    created.Add(new JObject { ["id"] = id, ["type"] = type, ["name"] = dispName, ["status"] = "created" });
                }

                // ── Wire connections ─────────────────────────────────────────────
                int wiresOk = 0, wiresFailed = 0;
                var wireErrors = new JArray();
                foreach (var wire in wires ?? new JArray())
                {
                    var fromStr = wire["from"]?.ToString();
                    var toStr   = wire["to"]?.ToString();
                    if (fromStr == null || toStr == null) continue;

                    var fp = fromStr.Split(':');
                    var tp = toStr.Split(':');
                    // Allow "compId" shorthand (no :index) — default to index 0
                    string fromId = fp[0], toId = tp[0];
                    int fi = fp.Length >= 2 && int.TryParse(fp[1], out var fii) ? fii : 0;
                    int ti = tp.Length >= 2 && int.TryParse(tp[1], out var tii) ? tii : 0;

                    if (!compMap.TryGetValue(fromId, out var fromComp))
                        { wiresFailed++; wireErrors.Add($"'{fromId}' not in compMap (not created?)"); continue; }
                    if (!compMap.TryGetValue(toId, out var toComp))
                        { wiresFailed++; wireErrors.Add($"'{toId}' not in compMap (not created?)"); continue; }

                    var fromParam = GhGetOutputParam(ghAsm, fromComp, fi);
                    var toParam   = GhGetInputParam(toComp, ti);

                    if (fromParam == null)
                        { wiresFailed++; wireErrors.Add($"'{fromId}' output[{fi}] is null"); continue; }
                    if (toParam == null)
                        { wiresFailed++; wireErrors.Add($"'{toId}' input[{ti}] is null (component may have fewer inputs)"); continue; }

                    try
                    {
                        // Use FirstOrDefault to avoid AmbiguousMatchException when GH has
                        // multiple AddSource overloads (AddSource(IGH_Param) vs AddSource(IGH_Param, int))
                        var addSource = toParam.GetType()
                            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                            .FirstOrDefault(m => m.Name == "AddSource" && m.GetParameters().Length == 1);
                        if (addSource == null) { wiresFailed++; wireErrors.Add($"AddSource not found on {toId} input[{ti}]"); continue; }
                        addSource.Invoke(toParam, new[] { fromParam });
                        wiresOk++;
                    }
                    catch (Exception ex) { wiresFailed++; wireErrors.Add($"{fromId}→{toId}: {ex.InnerException?.Message ?? ex.Message}"); }
                }

                // ── Solve ────────────────────────────────────────────────────────
                if (solve)
                    ghDoc.GetType().GetMethod("NewSolution", new[] { typeof(bool) })?.Invoke(ghDoc, new object[] { false });

                // ── Zoom canvas to fit all new components ─────────────────────────
                try
                {
                    var ghCanvas = instances?.GetProperty("ActiveCanvas", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                    if (ghCanvas != null)
                    {
                        var ct = ghCanvas.GetType();
                        // Try ZoomFit() — centres and fits all objects in view
                        var zoomFit = ct.GetMethod("ZoomFit", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null)
                                   ?? ct.GetMethod("FrameAll", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                        zoomFit?.Invoke(ghCanvas, null);
                        // Force a canvas redraw
                        ct.GetMethod("Refresh",    BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null)?.Invoke(ghCanvas, null);
                        ct.GetMethod("Invalidate", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null)?.Invoke(ghCanvas, null);
                    }
                }
                catch { }

                var msg = $"Built GH definition: {compMap.Count}/{components.Count} components";
                if (wires != null && wires.Count > 0)
                    msg += $", {wiresOk}/{wires.Count} wires connected";
                if (wiresFailed > 0) msg += $" ({wiresFailed} wires failed)";

                var result = new JObject { ["message"] = msg, ["components"] = created };
                if (wireErrors.Count > 0) result["wire_errors"] = wireErrors;
                return result;
            });
        }

        private static string CaptureAndAssess(string prompt)
        {
            var captureResult = CaptureViewport();
            JObject parsed;
            try { parsed = JObject.Parse(captureResult); }
            catch { return Fail("Failed to capture viewport."); }
            if (parsed["success"]?.ToObject<bool>() != true)
                return captureResult;

            var path = parsed["path"]?.ToString();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return Fail("Viewport capture file not found.");

            byte[] bytes;
            try
            {
                // Scale to max 1024 px longest side and re-encode as JPEG (quality 85)
                // to keep the base64 payload well under the 200k-token API limit.
                const int MaxDim = 1024;
                using (var original = System.Drawing.Image.FromFile(path))
                {
                    int w = original.Width, h = original.Height;
                    if (w > MaxDim || h > MaxDim)
                    {
                        double s = Math.Min((double)MaxDim / w, (double)MaxDim / h);
                        w = (int)(w * s);
                        h = (int)(h * s);
                    }
                    using (var scaled = new System.Drawing.Bitmap(original, w, h))
                    using (var ms = new System.IO.MemoryStream())
                    {
                        var jpegCodec = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders()
                            .FirstOrDefault(c => c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
                        var encParams = new System.Drawing.Imaging.EncoderParameters(1);
                        encParams.Param[0] = new System.Drawing.Imaging.EncoderParameter(
                            System.Drawing.Imaging.Encoder.Quality, 85L);
                        if (jpegCodec != null)
                            scaled.Save(ms, jpegCodec, encParams);
                        else
                            scaled.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                        bytes = ms.ToArray();
                    }
                }
            }
            catch (Exception ex) { return Fail($"Failed to process viewport image: {ex.Message}"); }

            var base64 = Convert.ToBase64String(bytes);
            return new JObject
            {
                ["success"]      = true,
                ["path"]         = path,
                ["base64"]       = base64,
                ["media_type"]   = "image/jpeg",
                ["prompt"]       = prompt ?? "Describe what you see in the 3D viewport.",
                ["vision_ready"] = true,
            }.ToString(Formatting.None);
        }

        private static string SolveGhDefinition()
        {
            return OnMain(() =>
            {
                var ghDoc = GetGhDocument();
                if (ghDoc == null) return new JObject { ["success"] = false, ["message"] = "No active Grasshopper canvas." };
                ghDoc.GetType().GetMethod("NewSolution", new[] { typeof(bool) })?.Invoke(ghDoc, new object[] { false });
                return Obj("message", "Grasshopper solution triggered.");
            });
        }

        private static string BakeGhDefinition(string layerName, int? r, int? g, int? b)
        {
            if (string.IsNullOrEmpty(layerName)) return Fail("layer_name is required.");
            return OnMain(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return new JObject { ["success"] = false, ["message"] = "No active Rhino document." };

                var ghDoc = GetGhDocument();
                if (ghDoc == null) return new JObject { ["success"] = false, ["message"] = "No active Grasshopper canvas." };

                // Ensure layer exists
                if (doc.Layers.FindName(layerName) == null)
                    doc.Layers.Add(new Rhino.DocObjects.Layer { Name = layerName });

                var targetLayerIndex = doc.Layers.FindName(layerName)?.Index ?? 0;
                var color = (r.HasValue && g.HasValue && b.HasValue)
                    ? (System.Drawing.Color?)System.Drawing.Color.FromArgb(r.Value, g.Value, b.Value)
                    : null;

                try
                {
                    var objects = GetGhObjects(ghDoc);
                    int bakedCount = 0;
                    foreach (var comp in objects)
                    {
                        if (comp == null) continue;
                        var t = comp.GetType();

                        var bakeMethod = t.GetMethod("BakeGeometry",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (bakeMethod == null) continue;

                        var attrs = new Rhino.DocObjects.ObjectAttributes();
                        attrs.LayerIndex = targetLayerIndex;
                        if (color.HasValue)
                        {
                            attrs.ColorSource = Rhino.DocObjects.ObjectColorSource.ColorFromObject;
                            attrs.ObjectColor = color.Value;
                        }

                        try
                        {
                            bakeMethod.Invoke(comp, new object[] { doc, attrs, new System.Collections.Generic.List<Guid>() });
                            bakedCount++;
                        }
                        catch { }
                    }
                    doc.Views.Redraw();
                    return Obj("message", $"Baked {bakedCount} components to layer '{layerName}'.");
                }
                catch (Exception ex)
                {
                    return new JObject { ["success"] = false, ["message"] = $"Bake failed: {ex.Message}" };
                }
            });
        }

        // ── GH definition builder helpers ────────────────────────────────────────

        private static object GhMakeSlider(Assembly ghAsm, string name, double value, double min, double max)
        {
            try
            {
                var t      = ghAsm.GetType("Grasshopper.Kernel.Special.GH_NumberSlider");
                var slider = Activator.CreateInstance(t);
                var slObj  = t.GetProperty("Slider", BindingFlags.Public | BindingFlags.Instance)?.GetValue(slider);
                if (slObj != null)
                {
                    var st = slObj.GetType();
                    st.GetProperty("Minimum")?.SetValue(slObj, (decimal)min);
                    st.GetProperty("Maximum")?.SetValue(slObj, (decimal)max);
                    st.GetProperty("Value")?.SetValue(slObj,   (decimal)value);
                }
                return slider;
            }
            catch { return null; }
        }

        private static object GhMakePanel(Assembly ghAsm, string name, string text)
        {
            try
            {
                var t     = ghAsm.GetType("Grasshopper.Kernel.Special.GH_Panel");
                var panel = Activator.CreateInstance(t);
                t.GetProperty("UserText", BindingFlags.Public | BindingFlags.Instance)?.SetValue(panel, text);
                return panel;
            }
            catch { return null; }
        }

        private static object GhMakeToggle(Assembly ghAsm, string name, bool value)
        {
            try
            {
                var t      = ghAsm.GetType("Grasshopper.Kernel.Special.GH_BooleanToggle");
                var toggle = Activator.CreateInstance(t);
                t.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance)?.SetValue(toggle, value);
                return toggle;
            }
            catch { return null; }
        }

        private static object GhMakeComponent(Assembly ghAsm, string componentName)
        {
            try
            {
                var instances = ghAsm.GetType("Grasshopper.Instances");
                var server    = instances?.GetProperty("ComponentServer", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var proxies   = server?.GetType()
                    .GetProperty("ObjectProxies", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(server) as IEnumerable;
                if (proxies == null) return null;

                // Three-pass fuzzy match: exact → starts-with → contains (shortest match wins)
                object exactProxy = null, startsProxy = null, containsProxy = null;
                int startsLen = int.MaxValue, containsLen = int.MaxValue;
                foreach (var p in proxies)
                {
                    if (p == null) continue;
                    var pt = p.GetType();
                    // Name may be directly on the proxy OR nested under Desc (IGH_InstanceDescription)
                    var n = pt.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)?.GetValue(p)?.ToString();
                    if (n == null)
                    {
                        var desc = pt.GetProperty("Desc", BindingFlags.Public | BindingFlags.Instance)?.GetValue(p);
                        n = desc?.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)?.GetValue(desc)?.ToString();
                    }
                    if (n == null) continue;
                    if (string.Equals(n, componentName, StringComparison.OrdinalIgnoreCase)) { exactProxy = p; break; }
                    if (startsProxy == null && n.StartsWith(componentName, StringComparison.OrdinalIgnoreCase) && n.Length < startsLen)
                        { startsProxy = p; startsLen = n.Length; }
                    if (n.IndexOf(componentName, StringComparison.OrdinalIgnoreCase) >= 0 && n.Length < containsLen)
                        { containsProxy = p; containsLen = n.Length; }
                }
                var proxy = exactProxy ?? startsProxy ?? containsProxy;
                if (proxy == null) return null;

                return proxy.GetType()
                    .GetMethod("CreateInstance", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null)
                    ?.Invoke(proxy, null);
            }
            catch { return null; }
        }

        private static object GhMakePython3(Assembly ghAsm, string name, string code, JArray inputs, JArray outputs)
        {
            try
            {
                // Try to create via component server by known Rhino-8 Python 3 component names
                object comp = GhMakeComponent(ghAsm, "Python 3 Script")
                           ?? GhMakeComponent(ghAsm, "Python3 Script")
                           ?? GhMakeComponent(ghAsm, "Python Script");

                // Fallback: try direct type instantiation (GH1 style)
                if (comp == null)
                {
                    var t = ghAsm.GetType("Grasshopper.Kernel.Components.GH_ScriptComponent")
                         ?? ghAsm.GetType("Grasshopper.Kernel.Special.GH_PythonScript");
                    if (t != null) comp = Activator.CreateInstance(t);
                }

                if (comp == null) return null;

                // Set code via ScriptSource or Script property
                var ct = comp.GetType();
                var codeProp = ct.GetProperty("ScriptSource", BindingFlags.Public | BindingFlags.Instance)
                            ?? ct.GetProperty("Script",       BindingFlags.Public | BindingFlags.Instance);
                if (codeProp != null)
                    codeProp.SetValue(comp, "#! python3\n" + code);

                // Rename the default x,y inputs to match the requested input names
                // GH python3 components have exactly 2 default inputs — we can only rename them, not add more
                if (inputs != null && inputs.Count > 0)
                {
                    try
                    {
                        var paramsObj = ct.GetProperty("Params", BindingFlags.Public | BindingFlags.Instance)?.GetValue(comp);
                        var inputList = paramsObj?.GetType().GetProperty("Input", BindingFlags.Public | BindingFlags.Instance)?.GetValue(paramsObj) as IList;
                        if (inputList != null)
                        {
                            for (int i = 0; i < Math.Min(inputs.Count, inputList.Count); i++)
                            {
                                var inputName = inputs[i]?.ToString() ?? $"x{i}";
                                var existing  = inputList[i];
                                existing?.GetType().GetProperty("NickName", BindingFlags.Public | BindingFlags.Instance)?.SetValue(existing, inputName);
                                existing?.GetType().GetProperty("Name",     BindingFlags.Public | BindingFlags.Instance)?.SetValue(existing, inputName);
                            }
                        }
                    }
                    catch { }
                }

                return comp;
            }
            catch { return null; }
        }

        private static object GhMakeComponentByGuid(Assembly ghAsm, string guidStr)
        {
            try
            {
                if (!Guid.TryParse(guidStr, out var guid)) return null;
                var instances = ghAsm.GetType("Grasshopper.Instances");
                var server    = instances?.GetProperty("ComponentServer", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var proxies   = server?.GetType()
                    .GetProperty("ObjectProxies", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(server) as System.Collections.IEnumerable;
                if (proxies == null) return null;

                foreach (var p in proxies)
                {
                    var pg = p?.GetType().GetProperty("Guid", BindingFlags.Public | BindingFlags.Instance)?.GetValue(p);
                    if (pg is Guid g && g == guid)
                        return p.GetType()
                            .GetMethod("CreateInstance", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null)
                            ?.Invoke(p, null);
                }
                return null;
            }
            catch { return null; }
        }

        private static void GhSetPivot(object comp, int x, int y)
        {
            try
            {
                comp.GetType().GetMethod("CreateAttributes", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null)?.Invoke(comp, null);
                var attrs = comp.GetType().GetProperty("Attributes", BindingFlags.Public | BindingFlags.Instance)?.GetValue(comp);
                attrs?.GetType().GetProperty("Pivot", BindingFlags.Public | BindingFlags.Instance)
                    ?.SetValue(attrs, new System.Drawing.PointF(x, y));
            }
            catch { }
        }

        private static object GhGetOutputParam(Assembly ghAsm, object comp, int index)
        {
            try
            {
                // Sliders, toggles, and panels expose themselves as the output param
                foreach (var specialType in new[] { "GH_NumberSlider", "GH_BooleanToggle", "GH_Panel" })
                {
                    var t = ghAsm.GetType("Grasshopper.Kernel.Special." + specialType);
                    if (t != null && t.IsAssignableFrom(comp.GetType())) return comp;
                }
                // Regular component: Params.Output[index]
                var paramsObj = comp.GetType().GetProperty("Params", BindingFlags.Public | BindingFlags.Instance)?.GetValue(comp);
                var output    = paramsObj?.GetType().GetProperty("Output", BindingFlags.Public | BindingFlags.Instance)?.GetValue(paramsObj) as IList;
                return (output != null && index < output.Count) ? output[index] : null;
            }
            catch { return null; }
        }

        private static object GhGetInputParam(object comp, int index)
        {
            try
            {
                var paramsObj = comp.GetType().GetProperty("Params", BindingFlags.Public | BindingFlags.Instance)?.GetValue(comp);
                var input     = paramsObj?.GetType().GetProperty("Input", BindingFlags.Public | BindingFlags.Instance)?.GetValue(paramsObj) as IList;
                return (input != null && index < input.Count) ? input[index] : null;
            }
            catch { return null; }
        }

        // ── Shared helpers ───────────────────────────────────────────────────────

        /// <summary>Returns the currently selected objects as a JArray for scene state tracking.</summary>
        private static JArray SelectedObjectsJson(RhinoDoc doc)
        {
            var arr = new JArray();
            foreach (var obj in doc.Objects.GetSelectedObjects(false, false))
                arr.Add(new JObject
                {
                    ["id"]    = obj.Id.ToString(),
                    ["type"]  = obj.Geometry?.GetType().Name ?? "unknown",
                    ["layer"] = doc.Layers[obj.Attributes.LayerIndex].Name,
                });
            return arr;
        }

        /// <summary>Collects Breps from a JArray of GUIDs. Returns false and sets err on failure.</summary>
        private static bool CollectBreps(RhinoDoc doc, JArray ids, out List<Brep> breps, out List<Guid> guids, out string err)
        {
            breps = new List<Brep>();
            guids = new List<Guid>();
            err   = null;
            foreach (var token in ids)
            {
                if (!Guid.TryParse(token.ToString(), out var g)) { err = $"Invalid GUID: {token}"; return false; }
                var obj = doc.Objects.Find(g);
                if (obj == null) { err = $"Object not found: {g}"; return false; }
                if (!(obj.Geometry is Brep b)) { err = $"Object {g} is not a Brep/solid."; return false; }
                breps.Add(b); guids.Add(g);
            }
            return true;
        }

        // ── RhinoCommon geometry helpers ─────────────────────────────────────────

        private static VolumeMassProperties ComputeVolume(GeometryBase geom)
        {
            try
            {
                if (geom is Brep      b) return VolumeMassProperties.Compute(b);
                if (geom is Mesh      m) return VolumeMassProperties.Compute(m);
                if (geom is Extrusion e) return VolumeMassProperties.Compute(e);
                return null;
            }
            catch { return null; }
        }

        private static AreaMassProperties ComputeArea(GeometryBase geom)
        {
            try
            {
                if (geom is Brep      b) return AreaMassProperties.Compute(b);
                if (geom is Mesh      m) return AreaMassProperties.Compute(m);
                if (geom is Surface   s) return AreaMassProperties.Compute(s);
                if (geom is Extrusion e) return AreaMassProperties.Compute(e);
                return null;
            }
            catch { return null; }
        }

        // ── Grasshopper helpers ──────────────────────────────────────────────────

        private static object GetGhDocument()
        {
            try
            {
                var gh      = Assembly.Load("Grasshopper");
                var instT   = gh.GetType("Grasshopper.Instances");
                var canvas  = instT?.GetProperty("ActiveCanvas", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                return canvas?.GetType().GetProperty("Document", BindingFlags.Public | BindingFlags.Instance)?.GetValue(canvas);
            }
            catch { return null; }
        }

        private static IEnumerable GetGhObjects(object ghDoc)
        {
            return ghDoc?.GetType()
                         .GetProperty("Objects", BindingFlags.Public | BindingFlags.Instance)
                         ?.GetValue(ghDoc) as IEnumerable
                   ?? Enumerable.Empty<object>();
        }

        private static T GProp<T>(object src, Type t, string prop)
        {
            var val = t.GetProperty(prop, BindingFlags.Public | BindingFlags.Instance)?.GetValue(src);
            if (val == null) return default(T);
            if (typeof(T) == typeof(string)) return (T)(object)val.ToString();
            return (T)val;
        }

        // ── Thread dispatch ──────────────────────────────────────────────────────

        private static string OnMain(Func<JObject> action)
        {
            JObject result = null;
            Exception err  = null;
            var done       = new ManualResetEventSlim(false);

            RhinoApp.InvokeOnUiThread(new Action(() =>
            {
                try   { result = action(); }
                catch (Exception ex) { err = ex; }
                finally { done.Set(); }
            }));

            done.Wait(TimeSpan.FromSeconds(30));

            if (err    != null) return new JObject { ["success"] = false, ["message"] = err.Message }.ToString(Formatting.None);
            if (result == null) return new JObject { ["success"] = false, ["message"] = "Operation timed out." }.ToString(Formatting.None);

            // Only set success=true if the action did not already set it to false explicitly
            if (result["success"] == null)
                result["success"] = true;
            return result.ToString(Formatting.None);
        }

        // ── JSON helpers ─────────────────────────────────────────────────────────

        /// <summary>Build a JObject from alternating key/value args, marked success=true.</summary>
        private static JObject Obj(params object[] kvPairs)
        {
            var o = new JObject();
            for (int i = 0; i + 1 < kvPairs.Length; i += 2)
                o[(string)kvPairs[i]] = kvPairs[i + 1] == null
                    ? JValue.CreateNull()
                    : JToken.FromObject(kvPairs[i + 1]);
            return o;
        }

        private static string Fail(string msg) =>
            new JObject { ["success"] = false, ["message"] = msg }.ToString(Formatting.None);

        // ── Input helpers ────────────────────────────────────────────────────────

        private static string S(JObject o, string key) => o?[key]?.ToString();

        private static double D(JObject o, string key)
        {
            var t = o?[key];
            if (t == null) return 0;
            return t.ToObject<double>();
        }
    }
}
