using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Rhino;
using Rhino.Commands;

namespace PenguinClaw
{
    public class PenguinClawScanCommand : Command
    {
        public override string EnglishName => "PenguinClawScan";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            try
            {
                RhinoApp.WriteLine("PenguinClawScan: Starting RhinoCommon + Grasshopper scan...");

                var rhinoClasses = ScanRhinoCommon();
                var installedComponents = ScanInstalledGrasshopperComponents();
                var activeCanvasComponents = ScanActiveCanvasComponents();

                RhinoApp.WriteLine($"PenguinClawScan: RhinoCommon classes={rhinoClasses.Count}");
                RhinoApp.WriteLine($"PenguinClawScan: Installed GH components={installedComponents.Count}");
                RhinoApp.WriteLine($"PenguinClawScan: Active GH canvas components={activeCanvasComponents.Count}");

                var payload = new ScanOutput
                {
                    generated_at_utc = DateTime.UtcNow.ToString("o"),
                    rhino_common = new RhinoCommonScan
                    {
                        assembly_name = typeof(RhinoDoc).Assembly.FullName,
                        public_class_count = rhinoClasses.Count,
                        public_method_count = rhinoClasses.Sum(c => c.public_methods != null ? c.public_methods.Count : 0),
                        classes = rhinoClasses,
                    },
                    grasshopper = new GrasshopperScan
                    {
                        installed_component_count = installedComponents.Count,
                        active_canvas_component_count = activeCanvasComponents.Count,
                        installed_components = installedComponents,
                        active_canvas_components = activeCanvasComponents,
                    }
                };

                var outputPath = ResolveScanOutputPath();
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                WriteJson(outputPath, payload);
                RhinoApp.WriteLine($"PenguinClawScan: Wrote scan JSON -> {outputPath}");

                var rebuildResult = TriggerRegistryRebuild();
                RhinoApp.WriteLine($"PenguinClawScan: /rebuild-registry -> {rebuildResult}");

                var totalItems = payload.rhino_common.public_class_count + payload.grasshopper.installed_component_count + payload.grasshopper.active_canvas_component_count;
                RhinoApp.WriteLine($"PenguinClawScan: Done. Total scanned items={totalItems}");
                return Result.Success;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"PenguinClawScan failed: {ex.Message}");
                return Result.Failure;
            }
        }

        private static List<RhinoClassInfo> ScanRhinoCommon()
        {
            var classes = new List<RhinoClassInfo>();
            var assembly = typeof(RhinoDoc).Assembly;
            var exportedTypes = assembly.GetExportedTypes().Where(t => t.IsClass && t.IsPublic).OrderBy(t => t.FullName);

            foreach (var type in exportedTypes)
            {
                var methods = type
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Where(m => !m.IsSpecialName)
                    .Select(m => m.Name)
                    .Distinct()
                    .OrderBy(n => n)
                    .ToList();

                classes.Add(new RhinoClassInfo
                {
                    name = type.FullName,
                    public_method_count = methods.Count,
                    public_methods = methods,
                });
            }

            return classes;
        }

        private static List<InstalledGhComponentInfo> ScanInstalledGrasshopperComponents()
        {
            var results = new List<InstalledGhComponentInfo>();
            try
            {
                var ghAssembly = Assembly.Load("Grasshopper");
                var instancesType = ghAssembly.GetType("Grasshopper.Instances");
                if (instancesType == null)
                    return results;

                var componentServerProp = instancesType.GetProperty("ComponentServer", BindingFlags.Public | BindingFlags.Static);
                var componentServer = componentServerProp?.GetValue(null, null);
                if (componentServer == null)
                    return results;

                var objectProxiesProp = componentServer.GetType().GetProperty("ObjectProxies", BindingFlags.Public | BindingFlags.Instance);
                var proxies = objectProxiesProp?.GetValue(componentServer, null) as IEnumerable;
                if (proxies == null)
                    return results;

                foreach (var proxy in proxies)
                {
                    if (proxy == null)
                        continue;

                    var proxyType = proxy.GetType();
                    var info = new InstalledGhComponentInfo
                    {
                        name = GetValue<string>(proxy, proxyType, "Name"),
                        category = GetValue<string>(proxy, proxyType, "Category"),
                        subcategory = GetValue<string>(proxy, proxyType, "SubCategory"),
                        description = GetValue<string>(proxy, proxyType, "Desc"),
                        guid = GetValue<object>(proxy, proxyType, "Guid")?.ToString(),
                        library_guid = GetValue<object>(proxy, proxyType, "LibraryGuid")?.ToString(),
                    };
                    results.Add(info);
                }
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"PenguinClawScan: GH installed component scan warning: {ex.Message}");
            }

            return results.OrderBy(r => r.category).ThenBy(r => r.subcategory).ThenBy(r => r.name).ToList();
        }

        private static List<ActiveGhComponentInfo> ScanActiveCanvasComponents()
        {
            var results = new List<ActiveGhComponentInfo>();
            try
            {
                var ghAssembly = Assembly.Load("Grasshopper");
                var instancesType = ghAssembly.GetType("Grasshopper.Instances");
                if (instancesType == null)
                    return results;

                var activeCanvasProp = instancesType.GetProperty("ActiveCanvas", BindingFlags.Public | BindingFlags.Static);
                var activeCanvas = activeCanvasProp?.GetValue(null, null);
                if (activeCanvas == null)
                    return results;

                var canvasType = activeCanvas.GetType();
                var documentProp = canvasType.GetProperty("Document", BindingFlags.Public | BindingFlags.Instance);
                var document = documentProp?.GetValue(activeCanvas, null);
                if (document == null)
                    return results;

                var objectsProp = document.GetType().GetProperty("Objects", BindingFlags.Public | BindingFlags.Instance);
                var objects = objectsProp?.GetValue(document, null) as IEnumerable;
                if (objects == null)
                    return results;

                foreach (var obj in objects)
                {
                    if (obj == null)
                        continue;

                    var objType = obj.GetType();
                    var info = new ActiveGhComponentInfo
                    {
                        name = GetPreferredName(obj, objType),
                        type = objType.FullName,
                        instance_guid = GetValue<object>(obj, objType, "InstanceGuid")?.ToString(),
                        component_guid = GetValue<object>(obj, objType, "ComponentGuid")?.ToString(),
                        inputs = GetParams(obj, objType, "Input"),
                        outputs = GetParams(obj, objType, "Output"),
                    };
                    results.Add(info);
                }
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"PenguinClawScan: GH canvas scan warning: {ex.Message}");
            }

            return results.OrderBy(r => r.name).ToList();
        }

        private static List<GhParamInfo> GetParams(object obj, Type objType, string inputOrOutput)
        {
            var paramsList = new List<GhParamInfo>();
            var paramsProp = objType.GetProperty("Params", BindingFlags.Public | BindingFlags.Instance);
            var paramsObj = paramsProp?.GetValue(obj, null);
            if (paramsObj == null)
                return paramsList;

            var ioProp = paramsObj.GetType().GetProperty(inputOrOutput, BindingFlags.Public | BindingFlags.Instance);
            var io = ioProp?.GetValue(paramsObj, null) as IEnumerable;
            if (io == null)
                return paramsList;

            foreach (var p in io)
            {
                if (p == null)
                    continue;
                var pt = p.GetType();
                paramsList.Add(new GhParamInfo
                {
                    name = GetValue<string>(p, pt, "Name"),
                    nickname = GetValue<string>(p, pt, "NickName"),
                    type = pt.FullName,
                });
            }

            return paramsList;
        }

        private static string GetPreferredName(object obj, Type type)
        {
            var nick = GetValue<string>(obj, type, "NickName");
            if (!string.IsNullOrWhiteSpace(nick))
                return nick;
            var name = GetValue<string>(obj, type, "Name");
            if (!string.IsNullOrWhiteSpace(name))
                return name;
            return type.Name;
        }

        private static T GetValue<T>(object source, Type sourceType, string propertyName)
        {
            var prop = sourceType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            if (prop == null)
                return default(T);

            var value = prop.GetValue(source, null);
            if (value == null)
                return default(T);

            if (typeof(T) == typeof(string))
                return (T)(object)value.ToString();

            return (T)value;
        }

        private static string ResolveScanOutputPath()
        {
            var pluginAssemblyDir = Path.GetDirectoryName(typeof(PenguinClawPlugin).Assembly.Location);
            var candidateRoots = new List<string>();

            if (!string.IsNullOrWhiteSpace(pluginAssemblyDir))
            {
                var current = new DirectoryInfo(pluginAssemblyDir);
                while (current != null)
                {
                    candidateRoots.Add(current.FullName);
                    current = current.Parent;
                }
            }

            var cwd = Directory.GetCurrentDirectory();
            if (!string.IsNullOrWhiteSpace(cwd))
                candidateRoots.Add(cwd);

            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrWhiteSpace(docs))
                candidateRoots.Add(Path.Combine(docs, "PenguinClaw"));

            foreach (var root in candidateRoots.Distinct())
            {
                try
                {
                    var agentDir = Path.Combine(root, "agent");
                    var toolsDir = Path.Combine(agentDir, "tools");
                    if (Directory.Exists(toolsDir))
                    {
                        return Path.Combine(toolsDir, "auto_generated", "scan_output.json");
                    }
                }
                catch
                {
                }
            }

            return Path.Combine(pluginAssemblyDir ?? cwd ?? ".", "scan_output.json");
        }

        private static void WriteJson(string path, ScanOutput payload)
        {
            var serializer = new DataContractJsonSerializer(typeof(ScanOutput));
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                serializer.WriteObject(stream, payload);
            }
        }

        private static string TriggerRegistryRebuild()
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create("http://localhost:8080/rebuild-registry");
                request.Method = "POST";
                request.ContentType = "application/json";
                var payload = System.Text.Encoding.UTF8.GetBytes("{}");
                request.ContentLength = payload.Length;
                using (var requestStream = request.GetRequestStream())
                {
                    requestStream.Write(payload, 0, payload.Length);
                }

                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    return $"{(int)response.StatusCode} {response.StatusCode} {reader.ReadToEnd()}";
                }
            }
            catch (Exception ex)
            {
                return $"failed ({ex.Message})";
            }
        }

        [DataContract]
        private class ScanOutput
        {
            [DataMember] public string generated_at_utc { get; set; }
            [DataMember] public RhinoCommonScan rhino_common { get; set; }
            [DataMember] public GrasshopperScan grasshopper { get; set; }
        }

        [DataContract]
        private class RhinoCommonScan
        {
            [DataMember] public string assembly_name { get; set; }
            [DataMember] public int public_class_count { get; set; }
            [DataMember] public int public_method_count { get; set; }
            [DataMember] public List<RhinoClassInfo> classes { get; set; }
        }

        [DataContract]
        private class GrasshopperScan
        {
            [DataMember] public int installed_component_count { get; set; }
            [DataMember] public int active_canvas_component_count { get; set; }
            [DataMember] public List<InstalledGhComponentInfo> installed_components { get; set; }
            [DataMember] public List<ActiveGhComponentInfo> active_canvas_components { get; set; }
        }

        [DataContract]
        private class RhinoClassInfo
        {
            [DataMember] public string name { get; set; }
            [DataMember] public int public_method_count { get; set; }
            [DataMember] public List<string> public_methods { get; set; }
        }

        [DataContract]
        private class InstalledGhComponentInfo
        {
            [DataMember] public string name { get; set; }
            [DataMember] public string category { get; set; }
            [DataMember] public string subcategory { get; set; }
            [DataMember] public string description { get; set; }
            [DataMember] public string guid { get; set; }
            [DataMember] public string library_guid { get; set; }
        }

        [DataContract]
        private class ActiveGhComponentInfo
        {
            [DataMember] public string name { get; set; }
            [DataMember] public string type { get; set; }
            [DataMember] public string instance_guid { get; set; }
            [DataMember] public string component_guid { get; set; }
            [DataMember] public List<GhParamInfo> inputs { get; set; }
            [DataMember] public List<GhParamInfo> outputs { get; set; }
        }

        [DataContract]
        private class GhParamInfo
        {
            [DataMember] public string name { get; set; }
            [DataMember] public string nickname { get; set; }
            [DataMember] public string type { get; set; }
        }
    }
}
