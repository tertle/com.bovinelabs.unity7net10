namespace Unity7Net10
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text.Json.Nodes;
    using UnityEditor;
    using UnityEditor.Compilation;
    using PackageInfo = UnityEditor.PackageManager.PackageInfo;
    using UnityEngine;
    using Debug = UnityEngine.Debug;

    internal static class Net10Setup
    {
        private const string MenuRoot = "Tools/Unity 7 .NET 10/";
        private static string ProjectPath => Path.GetDirectoryName(Application.dataPath);
        private static string PackagePath => PackageInfo.FindForAssembly(typeof(Net10Setup).Assembly).resolvedPath;

        [MenuItem(MenuRoot + "Setup or Update")]
        private static void SetupOrUpdate()
        {
            Setup(Array.Empty<string>());
        }

        [MenuItem(MenuRoot + "Enable Selected Assemblies")]
        private static void EnableSelected()
        {
            var paths = Selection.objects.Select(AssetDatabase.GetAssetPath)
                .Where(path => path.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase)).Distinct().ToArray();
            if (paths.Length == 0)
            {
                throw new InvalidOperationException("Select one or more asmdef assets, then choose Enable Selected Assemblies.");
            }

            Setup(paths);
        }

        [MenuItem(MenuRoot + "Regenerate IDE Projects")]
        private static void RegenerateProjects()
        {
            Unity.CodeEditor.CodeEditor.Editor.CurrentCodeEditor.SyncAll();
        }

        [MenuItem(MenuRoot + "Setup or Update", true)]
        [MenuItem(MenuRoot + "Enable Selected Assemblies", true)]
        private static bool CanSetup()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;
        }

        private static void Setup(string[] selected)
        {
            if ((Application.platform != RuntimePlatform.WindowsEditor && Application.platform != RuntimePlatform.LinuxEditor)
                || !Application.unityVersion.StartsWith("7000.", StringComparison.Ordinal))
            {
                throw new NotSupportedException("This prototype requires Unity 7 on Windows or Linux and its bundled .NET 10 SDK.");
            }

            foreach (var assetPath in selected)
            {
                if ((!assetPath.StartsWith("Assets/", StringComparison.Ordinal) && !assetPath.StartsWith("Packages/", StringComparison.Ordinal))
                    || !File.Exists(FullPath(assetPath))
                    || Path.GetFileName(assetPath) == "Unity7Net10.Setup.Editor.asmdef")
                {
                    throw new InvalidOperationException("Only writable project or embedded-package asmdefs can be opted in: " + assetPath);
                }
            }

            BuildTools();
            var responseContents = string.Join(
                Environment.NewLine,
                Net10Bootstrap.ResponseMarker,
                "@\".unity7-net10/references.rsp\"",
                "/langversion:" + Net10Bootstrap.LanguageVersion,
                string.Empty);
            foreach (var assetPath in selected)
            {
                var response = Path.Combine(Path.GetDirectoryName(FullPath(assetPath)), "csc.rsp");
                var existing = File.Exists(response) ? File.ReadAllText(response) : string.Empty;
                if (!existing.Contains(Net10Bootstrap.ResponseMarker))
                {
                    Backup(response);
                    File.WriteAllText(response, existing.TrimEnd() + Environment.NewLine + responseContents);
                }
            }

            var marked = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => File.Exists(FullPath(path)) && Net10Bootstrap.IsOptedIn(FullPath(path))).ToList();
            foreach (var assetPath in selected)
            {
                if (!marked.Contains(assetPath))
                {
                    marked.Add(assetPath);
                }
            }

            if (marked.Count == 0)
            {
                var folder = AssetDatabase.GenerateUniqueAssetPath("Assets/Net10");
                Directory.CreateDirectory(FullPath(folder));
                var assetPath = folder + "/" + Path.GetFileName(folder) + ".asmdef";
                File.WriteAllText(FullPath(assetPath), "{\"name\":\"" + Path.GetFileName(folder) + "\",\"autoReferenced\":false}");
                File.WriteAllText(FullPath(folder + "/csc.rsp"), responseContents);
                marked.Add(assetPath);
            }

            foreach (var assetPath in marked)
            {
                var path = FullPath(assetPath);
                var response = Path.Combine(Path.GetDirectoryName(path), "csc.rsp");
                var lines = File.ReadAllLines(response);
                if (lines.Any(line => line.Trim() == "/langversion:11.0"))
                {
                    Backup(response);
                    File.WriteAllLines(response, lines.Select(line => line.Trim() == "/langversion:11.0"
                        ? "/langversion:" + Net10Bootstrap.LanguageVersion : line));
                }

                var definition = JsonNode.Parse(File.ReadAllText(path));
                if (definition["name"].GetValue<string>() == "Unity7Net10.Setup.Editor")
                {
                    throw new InvalidOperationException("The setup assembly must remain on Unity's ordinary profile.");
                }

                var constraints = definition["defineConstraints"]?.AsArray().Select(node => node.GetValue<string>()).ToList()
                    ?? new System.Collections.Generic.List<string>();
                if (!constraints.Contains(Net10Bootstrap.ActivationDefine))
                {
                    Backup(path);
                    constraints.Add(Net10Bootstrap.ActivationDefine);
                    definition["defineConstraints"] = new JsonArray(constraints.Select(value => (JsonNode)JsonValue.Create(value)).ToArray());
                    File.WriteAllText(path, definition.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                }
            }

            var ignorePath = Path.Combine(ProjectPath, ".gitignore");
            var ignore = File.Exists(ignorePath) ? File.ReadAllLines(ignorePath) : Array.Empty<string>();
            if (!ignore.Contains("/.unity7-net10/"))
            {
                File.AppendAllText(ignorePath, Environment.NewLine + "/.unity7-net10/" + Environment.NewLine);
            }

            Net10Bootstrap.Initialize();
            AssetDatabase.Refresh();
            CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache);
            Debug.Log("Unity 7 .NET 10: configured " + marked.Count + " assembly(s). Standalone uses CoreCLR. Use Enable Selected Assemblies to configure more asmdefs.");
        }

        private static void BuildTools()
        {
            var editorData = Path.Combine(Path.GetDirectoryName(EditorApplication.applicationPath), "Data");
            var sdkRoot = Path.Combine(editorData, "DotNetSdk");
            var referencePack = LatestVersion(Path.Combine(sdkRoot, "packs", "Microsoft.NETCore.App.Ref"));
            referencePack = Path.Combine(referencePack, "ref", "net10.0");
            var compiler = Path.Combine(LatestVersion(Path.Combine(sdkRoot, "sdk")), "Roslyn", "bincore", "csc.dll");
            var dotnet = Path.Combine(sdkRoot, Net10Bootstrap.DotNetExecutable);
            var tool = Net10Bootstrap.ToolDirectory;
            Directory.CreateDirectory(tool);
            var response = Path.Combine(tool, "references.rsp");
            File.WriteAllLines(response, Directory.GetFiles(referencePack, "*.dll").OrderBy(Path.GetFileName, StringComparer.Ordinal)
                .Select(path => "/reference:\"" + path + "\""));

            var startInfo = new ProcessStartInfo(dotnet)
            {
                Arguments = "\"" + compiler + "\" /nologo /noconfig /nostdlib+ /deterministic+ /langversion:9.0 /target:exe /out:\""
                    + Path.Combine(tool, "Unity7Net10.BuildProgram.dll") + "\" @\"" + response + "\" \""
                    + Path.Combine(PackagePath, "Tools~", "BuildProgram.cs") + "\"",
                WorkingDirectory = ProjectPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using (var process = Process.Start(startInfo))
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException("Build-program compilation failed: " + output.Result + error.Result);
                }
            }

            File.WriteAllText(Path.Combine(tool, "Unity7Net10.BuildProgram.runtimeconfig.json"),
                "{\"runtimeOptions\":{\"tfm\":\"net10.0\",\"framework\":{\"name\":\"Microsoft.NETCore.App\",\"version\":\"10.0.0\"},\"rollForward\":\"LatestPatch\"}}");
            File.WriteAllText(Path.Combine(tool, "profile.json"), JsonUtility.ToJson(new Net10Bootstrap.ProfileConfiguration
            {
                originalProgram = Path.Combine(editorData, "Tools", "BuildPipeline", "ScriptCompilationBuildProgram.exe"),
                dotnet = dotnet,
                referencePack = referencePack,
            }, true));
        }

        private static string LatestVersion(string directory)
        {
            return Directory.GetDirectories(directory)
                .Where(path => Version.TryParse(Path.GetFileName(path), out var version) && version.Major == 10)
                .OrderByDescending(path => Version.Parse(Path.GetFileName(path))).First();
        }

        private static string FullPath(string assetPath)
        {
            return Path.Combine(ProjectPath, assetPath);
        }

        private static void Backup(string path)
        {
            if (!File.Exists(path))
            {
                return;
            }

            var backup = Path.Combine(Net10Bootstrap.ToolDirectory, "backups", path.Substring(ProjectPath.Length + 1));
            if (!File.Exists(backup))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                File.Copy(path, backup);
            }
        }
    }
}


