namespace Unity7Net10
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Xml.Linq;
    using UnityEditor;
    using UnityEngine;

    internal sealed class Net10ProjectGeneration : AssetPostprocessor
    {
        private static string OnGeneratedCSProject(string path, string content)
        {
            var projectName = Path.GetFileNameWithoutExtension(path);
            var asmdef = UnityEditor.Compilation.CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(projectName);
            var configurationPath = Path.Combine(Net10Bootstrap.ToolDirectory, "profile.json");
            if (!Net10Bootstrap.IsOptedIn(asmdef) || !File.Exists(configurationPath))
            {
                return content;
            }


            var configuration = JsonUtility.FromJson<ProfileConfiguration>(File.ReadAllText(configurationPath));
            var referencePack = configuration.referencePack;
            var packReferences = Directory.GetFiles(referencePack, "*.dll").OrderBy(Path.GetFileName, StringComparer.Ordinal).ToArray();
            var packNames = new HashSet<string>(packReferences.Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase);
            var document = XDocument.Parse(content);
            var project = document.Root;

            // Modern SDK projects describe the same explicit framework surface as the Unity wrapper.
            foreach (var element in project.DescendantsAndSelf())
            {
                element.Name = element.Name.LocalName;
            }

            project.Attributes().Where(attribute => attribute.IsNamespaceDeclaration).Remove();
            project.Attribute("ToolsVersion")?.Remove();
            project.Attribute("DefaultTargets")?.Remove();
            project.Attribute("Sdk")?.Remove();
            project.Elements("Import").Where(import =>
                (string)import.Attribute("Sdk") == "Microsoft.NET.Sdk"
                || ((string)import.Attribute("Project")).EndsWith("Microsoft.CSharp.targets", StringComparison.OrdinalIgnoreCase)).Remove();

            var obsoleteProperties = new HashSet<string>(StringComparer.Ordinal)
            {
                "LangVersion",
                "TargetFramework",
                "TargetFrameworkVersion",
                "TargetFrameworkIdentifier",
                "TargetFrameworkProfile",
                "_TargetFrameworkDirectories",
                "_FullFrameworkReferenceAssemblyPaths",
                "ResolveNuGetPackages",
                "DisableHandlePackageFileConflicts",
            };
            project.Elements("PropertyGroup").Elements().Where(property => obsoleteProperties.Contains(property.Name.LocalName)).Remove();

            foreach (var reference in project.Descendants("Reference").ToArray())
            {
                var hint = (string)reference.Element("HintPath");
                if (string.IsNullOrEmpty(hint))
                {
                    continue;
                }

                var normalized = hint.Replace('\\', '/');
                var legacyFramework = normalized.Contains("/UnityReferenceAssemblies/", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains("/NetStandard/", StringComparison.OrdinalIgnoreCase);
                var supersededExtension = normalized.Contains("/BCLExtensions/TargetingPacks/", StringComparison.OrdinalIgnoreCase)
                    && packNames.Contains(Path.GetFileName(hint));
                var existingPack = string.Equals(Path.GetDirectoryName(hint), referencePack, StringComparison.OrdinalIgnoreCase);
                if (legacyFramework || supersededExtension || existingPack)
                {
                    reference.Remove();
                }
            }

            var properties = new XElement("PropertyGroup",
                new XElement("TargetFramework", "net10.0"),
                new XElement("LangVersion", Net10Bootstrap.LanguageVersion),
                new XElement("EnableDefaultItems", "false"),
                new XElement("GenerateAssemblyInfo", "false"),
                new XElement("GenerateTargetFrameworkAttribute", "false"),
                new XElement("DisableImplicitFrameworkReferences", "true"),
                new XElement("DisableImplicitFrameworkDefines", "true"),
                new XElement("NoStandardLibraries", "true"),
                new XElement("AppendTargetFrameworkToOutputPath", "false"));

            // Set per-project intermediates before Sdk.props imports Microsoft.Common.props.
            project.AddFirst(
                new XElement("PropertyGroup",
                    new XElement("BaseIntermediateOutputPath", @"Temp\obj\$(MSBuildProjectName)\"),
                    new XElement("MSBuildProjectExtensionsPath", "$(BaseIntermediateOutputPath)")),
                new XElement("Import", new XAttribute("Project", "Sdk.props"), new XAttribute("Sdk", "Microsoft.NET.Sdk")));
            project.Add(properties);
            project.Add(new XElement("ItemGroup", packReferences.Select(reference =>
                new XElement("Reference",
                    new XAttribute("Include", Path.GetFileNameWithoutExtension(reference)),
                    new XElement("HintPath", reference),
                    new XElement("Private", "false")))));
            project.Add(new XElement("Import", new XAttribute("Project", "Sdk.targets"), new XAttribute("Sdk", "Microsoft.NET.Sdk")));
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine + project;
        }

        [Serializable]
        private sealed class ProfileConfiguration
        {
            public string referencePack;
        }
    }
}
