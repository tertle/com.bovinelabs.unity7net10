namespace Unity7Net10
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using UnityEditor;
    using UnityEditor.Build;
    using UnityEditor.Compilation;
    using UnityEngine;

    [InitializeOnLoad]
    internal static class Net10Bootstrap
    {
        internal const string ActivationDefine = "UNITY7_NET10_ENABLED";
        internal const string ResponseMarker = "/define:UNITY7_NET10";
        internal const string LanguageVersion = "14.0";

        internal static string ToolDirectory => Path.Combine(Path.GetDirectoryName(Application.dataPath), ".unity7-net10");

        internal static string DotNetExecutable => Application.platform == RuntimePlatform.WindowsEditor ? "dotnet.exe" : "dotnet";

        static Net10Bootstrap()
        {
            // Compilation can start before delayCall after a domain reload (including player builds).
            // Reinstall the hook synchronously; defer settings changes until the Editor is ready.
            if (File.Exists(Path.Combine(ToolDirectory, "profile.json")))
            {
                InstallCompilationHook();
            }

            EditorApplication.delayCall += Initialize;
            EditorApplication.quitting += OnQuitting;
        }

        internal static bool IsOptedIn(string asmdef)
        {
            if (string.IsNullOrEmpty(asmdef))
            {
                return false;
            }

            var response = Path.Combine(Path.GetDirectoryName(asmdef), "csc.rsp");
            return File.Exists(response) && File.ReadAllText(response).Contains(ResponseMarker);
        }

        internal static void Initialize()
        {
            var configurationPath = Path.Combine(ToolDirectory, "profile.json");
            if (!File.Exists(configurationPath))
            {
                return;
            }


            var config = JsonUtility.FromJson<ProfileConfiguration>(File.ReadAllText(configurationPath));
            var editorData = Path.Combine(Path.GetDirectoryName(EditorApplication.applicationPath), "Data");
            config.originalProgram = Path.Combine(editorData, "Tools", "BuildPipeline", "ScriptCompilationBuildProgram.exe");
            config.dotnet = Path.Combine(editorData, "DotNetSdk", DotNetExecutable);
            var packs = Path.Combine(editorData, "DotNetSdk", "packs", "Microsoft.NETCore.App.Ref");
            config.referencePack = Directory.GetDirectories(packs)
                .Where(path => Version.TryParse(Path.GetFileName(path), out var version) && version.Major == 10)
                .OrderByDescending(path => Version.Parse(Path.GetFileName(path)))
                .Select(path => Path.Combine(path, "ref", "net10.0")).First();
            File.WriteAllText(configurationPath, JsonUtility.ToJson(config, true));
            File.WriteAllLines(Path.Combine(ToolDirectory, "references.rsp"), Directory.GetFiles(config.referencePack, "*.dll")
                .OrderBy(Path.GetFileName, StringComparer.Ordinal).Select(path => "/reference:\"" + path + "\""));

            InstallCompilationHook();

            var defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone).Split(';').Where(value => !string.IsNullOrEmpty(value)).ToList();
            if (!defines.Contains(ActivationDefine))
            {
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.CoreCLR);
                defines.Add(ActivationDefine);
                PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, defines.ToArray());
                CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache);
            }
        }

        private static void InstallCompilationHook()
        {
            var type = typeof(CompilationPipeline).Assembly.GetType("UnityEditor.Scripting.ScriptCompilation.EditorCompilation");
            var program = type.GetProperty("ScriptCompilationBuildProgram", BindingFlags.Static | BindingFlags.Public).GetValue(null);
            var arguments = (string[])program.GetType().GetField("<AlwaysArguments>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(program);
            arguments[0] = "\"" + Path.Combine(ToolDirectory, "Unity7Net10.BuildProgram.dll") + "\"";
        }

        private static void OnQuitting()
        {
            var defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone).Split(';')
                .Where(value => !string.IsNullOrEmpty(value) && value != ActivationDefine).ToArray();
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, defines);
        }

        [Serializable]
        internal sealed class ProfileConfiguration
        {
            public string originalProgram;
            public string dotnet;
            public string referencePack;
        }
    }
}



