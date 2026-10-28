# Unity 7 .NET 10

An **Editor-only UPM package** enabling .NET 10 APIs and C# 14 in selected assemblies, including matching IL postprocessing references and generated IDE projects.

Experimental. Validated on Windows / Unity 7000.0.0a12 and its .NET 10 CoreCLR runtime. C# 14 uses this Editor's bundled compiler; earlier Editors need separate validation. Setup also permits Linux Editors and selects their bundled dotnet host; Linux setup, cold restart, IDE generation, and standalone players remain unverified. IL2CPP, Burst jobs, other platforms, and later Unity internals are unverified.

## Setup

1. Clone this repository directly into your Unity project's **Packages/com.bovinelabs.unity7net10** directory. The repository root is the package; do not add another package subfolder.
2. Open Unity and choose **Tools > Unity 7 .NET 10 > Setup or Update**.

That's the setup. No PowerShell, Unity CLI, Pipeline, external SDK, or NuGet restore.

If no assemblies are opted in yet, Setup creates an empty **Assets/Net10** code folder and asmdef. Put modern runtime code there. It is autoReferenced: false; consuming asmdefs need explicit references.

## Existing assemblies

Select one or more asmdef assets and choose **Tools > Unity 7 .NET 10 > Enable Selected Assemblies**.

The menu generates a **csc.rsp** beside each selected asmdef. Select multiple asmdefs to enable them together. Existing compiler arguments and asmdef settings are preserved, with originals backed up in .unity7-net10/backups.

Each generated csc.rsp points to .unity7-net10/references.rsp under the Unity project root and selects C# 14. Setup creates this shared reference list from the installed Editor; configured startups refresh it. The menu adds UNITY7_NET10_ENABLED to each opted-in asmdef's defineConstraints for reliable startup. Opt in assemblies consuming modern public APIs too. Default Assembly-CSharp assemblies cannot use this asmdef startup gate; use an asmdef instead.

Run Setup before adding modern API code to an unconfigured assembly. For assemblies added later, select their asmdefs and choose Enable Selected Assemblies. The menus generate the response files; no manual copying is needed.

## Updates and IDE

After pulling package updates or changing the Editor version, choose **Setup or Update** again. Setup migrates the package's existing `/langversion:11.0` entries to `/langversion:14.0`, preserving other compiler arguments. **Regenerate IDE Projects** is also available in the same menu.

The two Editor bootstrap/project-generation scripts and setup menu compile inside Unity on its normal profile. The build-program wrapper compiles with Unity's bundled SDK and lives outside asset import under .unity7-net10. No framework DLLs are imported as Unity plugins.

The wrapper replaces framework references only for assemblies marked by their generated response file. Unity references and unmarked packages keep their reference sets. Opted-in IDE projects target net10.0 / C# 14.

Configured projects install the private compilation hook synchronously during package initialization. Deferring it solely to an Editor delay callback allowed player builds after a domain reload to bypass the wrapper on alpha 12, leaving IL postprocessors with legacy framework references.

Standalone is configured to use CoreCLR. The activation define is removed on graceful Editor quit so the bootstrap loads before modern source at the next startup. Commit the package and opted-in asmdef/response-file changes; generated local tools are ignored. Fresh clones run Setup once.

This depends on a private Unity compilation hook and is a prototype, not a supported Unity API profile switch.

## Verification

Validated on Unity 7000.0.0a12: menu setup/update, default code-folder creation, migration from C# 11 to 14, and IDE generation as net10.0 / C# 14 with 167 modern framework references. The Editor bundles SDK 10.0.303, Roslyn 5.6.0-2.26377.103, and reference pack 10.0.11; its runtime reports .NET 10.0.10-dev.

C# 14 field-backed properties, extension properties, and null conditional assignment, plus collection expressions, INumber<T>, LeftJoin, OrderedDictionary, and Lock passed in the Editor and a built Windows CoreCLR player. Unmarked assembly descriptions were unchanged. The player build completed with zero errors or warnings.

Earlier validation on alpha 9 covered selected-asmdef settings preservation, cold restart, and Rider generation with C# 11. Cold restart and those preservation cases have not been rerun on alpha 12.
