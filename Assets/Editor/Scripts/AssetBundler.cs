﻿using UnityEngine;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using System.Linq;
using System;
using System.Reflection;

/// <summary>
/// 
/// The AssetBundler does several useful things when preparing your mod:
/// 
/// 1. Modifies all non-Editor MonoScript files to reference ASSEMBLY_NAME rather than Assembly-CSharp.
///     - At runtime in the game, this new assembly will be used to resolve the script references.
/// 2. Builds your project as ASSEMBLY_NAME.dll rather than Assembly-CSharp.dll.
///     - Having a name distinct from "Assembly-CSharp.dll" is required in order to load the mod in the game.
/// 3. Copies any managed assemblies from Assets/Plugins to the output folder for inclusion alongside your bundle.
/// 4. Builds the AssetBundle and copies the relevant .bundle file to the final output folder.
/// 5. Restores MonoScript references to Assembly-CSharp so they can be found by the Unity Editor again.
/// 
/// </summary>
public class AssetBundler
{
    /// <summary>
    /// Temporary location for building AssetBundles
    /// </summary>
    static string TEMP_BUILD_FOLDER = "Temp/AssetBundles";

    /// <summary>
    /// List of managed assemblies to ignore in the build (because they already exist in KTaNE itself)
    /// </summary>
    static List<string> EXCLUDED_ASSEMBLIES = new List<string> { "KMFramework.dll" };

    /// <summary>
    /// Location of MSBuild.exe tool
    /// </summary>
    static string MSBUILD_PATH = "C:\\Program Files (x86)\\MSBuild\\14.0\\Bin\\MSBuild.exe";

    /// <summary>
    /// Name of the bundle file produced. This relies on the AssetBundle tag used, which is set to mod.bundle by default.
    /// </summary>
    static string BUNDLE_FILENAME = "mod.bundle";


    #region Internal bundler Variables
    /// <summary>
    /// The name of the mod's main assembly
    /// </summary>
    private string assemblyName;

    /// <summary>
    /// Output folder for the final asset bundle file
    /// </summary>
    private string outputFolder;

    /// <summary>
    /// List of MonoScripts modified during the bundling process that need to be restored after.
    /// </summary>
    private List<string> scriptPathsToRestore = new List<string>();
    #endregion

    [MenuItem("Keep Talking ModKit/Build AssetBundle")]
    public static void BuildAllAssetBundles_WithEditorUtility()
    {
        BuildModBundle(false);
    }

    [MenuItem("Keep Talking ModKit/Build AssetBundle (with MSBuild)")]
    public static void BuildAllAssetBundles_MSBuild()
    {
        BuildModBundle(true);
    }

    protected static void BuildModBundle(bool useMSBuild)
    {
        Debug.LogFormat("Creating \"{0}\" AssetBundle...", BUNDLE_FILENAME);

        if (ModConfig.Instance == null 
            || ModConfig.ID == ""
            || ModConfig.OutputFolder == "")
        {
            Debug.LogError("You must configure your mod from the \"Keep Talking ModKit / Configure Mod\" menu first.");
            return;
        }

        AssetBundler bundler = new AssetBundler();

        bundler.assemblyName = ModConfig.ID;
        bundler.outputFolder = ModConfig.OutputFolder + "/" + bundler.assemblyName;

        bool success = false;

        try
        {
            bundler.WarnIfAssetsAreNotTagged();
            bundler.CheckForAssets();

            //Delete the cotnents of OUTPUT_FOLDER
            bundler.CleanBuildFolder();

            //Change all non-Editor scripts to reference ASSEMBLY_NAME instead of Assembly-CSharp
            bundler.AdjustMonoScripts();

            //Build the assembly using either MSBuild or Unity EditorUtility methods
            if (useMSBuild)
            {
                bundler.CompileAssemblyWithMSBuild();
            }
            else
            {
                bundler.CompileAssemblyWithEditor();
            }

            //Copy any other non-Editor managed assemblies to the output folder
            bundler.CopyManagedAssemblies();

            //Create the modInfo.json file
            bundler.CreateModInfo();

            //Lastly, create the asset bundle itself and copy it to the output folder
            bundler.CreateAssetBundle();

            success = true;
        }
        catch (Exception e)
        {
            Debug.LogErrorFormat("Failed to build AssetBundle: {0}\n{1}", e.Message, e.StackTrace);
        }
        finally
        {
            //Restore script references to Assembly-CSharp, as expected by the Unity Editor
            bundler.RestoreMonoScripts();
        }

        if (success)
        {
            Debug.LogFormat("Build complete! Output: {0}", bundler.outputFolder);
        }
    }

    /// <summary>
    /// Delete and recreate the OUTPUT_FOLDER to ensure a clean build.
    /// </summary>
    protected void CleanBuildFolder()
    {
        Debug.LogFormat("Cleaning {0}...", outputFolder);

        if (Directory.Exists(outputFolder))
        {
            Directory.Delete(outputFolder, true);
        }

        Directory.CreateDirectory(outputFolder);
    }

    /// <summary>
    /// Build the ASSEMBLY_NAME.dll from the project's scripts using MSBuild.
    /// </summary>
    void CompileAssemblyWithMSBuild()
    {
        Debug.Log("Compiling scripts with MSBuild...");

        IEnumerable<string> scriptAssetPaths = AssetDatabase.GetAllAssetPaths().Where(assetPath => assetPath.EndsWith(".cs") && !assetPath.StartsWith("Assets/Editor"));

        if (scriptAssetPaths.Count() == 0)
        {
            Debug.LogFormat("No scripts found to compile.");
            return;
        }

        if (!File.Exists(MSBUILD_PATH))
        {
            throw new Exception("MSBUILD_PATH not set to your MSBuild.exe");
        }

        //modify the csproj (if needed)
        var csproj = File.ReadAllText("PianoKeys.CSharp.csproj");
        csproj = csproj.Replace("<AssemblyName>Assembly-CSharp</AssemblyName>", "<AssemblyName>"+ assemblyName + "</AssemblyName>");
        File.WriteAllText("modkithelper.CSharp.csproj", csproj);

        string path = "modkithelper.CSharp.csproj";
        System.Diagnostics.Process p = new System.Diagnostics.Process();
        p.StartInfo.FileName = MSBUILD_PATH;
        p.StartInfo.Arguments = path + " /p:Configuration=Release";
        p.StartInfo.UseShellExecute = false;
        p.StartInfo.RedirectStandardOutput = false;
        p.StartInfo.RedirectStandardError = false;
        p.StartInfo.CreateNoWindow = true;
        p.Start();
        p.WaitForExit();

        string source = string.Format("Temp/UnityVS_bin/Release/{0}.dll", assemblyName);
        string dest = Path.Combine(outputFolder, assemblyName + ".dll");
        File.Copy(source, dest);
    }

    /// <summary>
    /// Build the ASSEMBLY_NAME.dll from the project's scripts using EditorUtility.CompileCSharp().
    /// </summary>
    void CompileAssemblyWithEditor()
    {
        Debug.Log("Compiling scripts with EditorUtility.CompileCSharp...");
        IEnumerable<string> scriptAssetPaths = AssetDatabase.GetAllAssetPaths().Where(assetPath => assetPath.EndsWith(".cs") && !assetPath.StartsWith("Assets/Editor"));

        if (scriptAssetPaths.Count() == 0)
        {
            Debug.LogFormat("No scripts found to compile.");
            return;
        }

        string playerDefines = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone);

        if (playerDefines.Length > 0 && !playerDefines.EndsWith(";"))
        {
            playerDefines += ";";
        }

        string allDefines = playerDefines + "TRACE;UNITY_5_3_OR_NEWER;UNITY_5_3_5;UNITY_5_3;UNITY_5;UNITY_64;ENABLE_NEW_BUGREPORTER;ENABLE_AUDIO;ENABLE_CACHING;ENABLE_CLOTH;ENABLE_DUCK_TYPING;ENABLE_FRAME_DEBUGGER;ENABLE_GENERICS;ENABLE_HOME_SCREEN;ENABLE_IMAGEEFFECTS;ENABLE_LIGHT_PROBES_LEGACY;ENABLE_MICROPHONE;ENABLE_MULTIPLE_DISPLAYS;ENABLE_PHYSICS;ENABLE_PLUGIN_INSPECTOR;ENABLE_SHADOWS;ENABLE_SINGLE_INSTANCE_BUILD_SETTING;ENABLE_SPRITERENDERER_FLIPPING;ENABLE_SPRITES;ENABLE_SPRITE_POLYGON;ENABLE_TERRAIN;ENABLE_RAKNET;ENABLE_UNET;ENABLE_UNITYEVENTS;ENABLE_VR;ENABLE_WEBCAM;ENABLE_WWW;ENABLE_CLOUD_SERVICES;ENABLE_CLOUD_SERVICES_ADS;ENABLE_CLOUD_HUB;ENABLE_CLOUD_PROJECT_ID;ENABLE_CLOUD_SERVICES_PURCHASING;ENABLE_CLOUD_SERVICES_ANALYTICS;ENABLE_CLOUD_SERVICES_UNET;ENABLE_CLOUD_SERVICES_BUILD;ENABLE_CLOUD_LICENSE;ENABLE_EDITOR_METRICS;ENABLE_EDITOR_METRICS_CACHING;INCLUDE_DYNAMIC_GI;INCLUDE_GI;INCLUDE_IL2CPP;INCLUDE_DIRECTX12;PLATFORM_SUPPORTS_MONO;RENDER_SOFTWARE_CURSOR;ENABLE_LOCALIZATION;ENABLE_ANDROID_ATLAS_ETC1_COMPRESSION;ENABLE_EDITOR_TESTS_RUNNER;UNITY_STANDALONE_WIN;UNITY_STANDALONE;ENABLE_SUBSTANCE;ENABLE_TEXTUREID_MAP;ENABLE_RUNTIME_GI;ENABLE_MOVIES;ENABLE_NETWORK;ENABLE_CRUNCH_TEXTURE_COMPRESSION;ENABLE_LOG_MIXED_STACKTRACE;ENABLE_UNITYWEBREQUEST;ENABLE_EVENT_QUEUE;ENABLE_CLUSTERINPUT;ENABLE_WEBSOCKET_HOST;ENABLE_MONO;ENABLE_PROFILER;DEBUG;TRACE;UNITY_ASSERTIONS;UNITY_EDITOR;UNITY_EDITOR_64;UNITY_EDITOR_WIN";
        string outputFilename = outputFolder + "/" + assemblyName + ".dll";

        List<string> managedReferences = AssetDatabase.GetAllAssetPaths()
            .Where(path => path.EndsWith(".dll") && path.StartsWith("Assets/Plugins/Managed"))
            .Select(path => "Assets/Plugins/Managed/" + Path.GetFileNameWithoutExtension(path))
            .ToList();

        managedReferences.Add("Library/UnityAssemblies/UnityEngine");
        managedReferences.Add("Library/UnityAssemblies/UnityEngine.UI");

        var compilerOutput = EditorUtility.CompileCSharp(
            scriptAssetPaths.ToArray(),
            managedReferences.ToArray(),
            allDefines.Split(';'),
            outputFilename);

        foreach (var log in compilerOutput)
        {
            Debug.LogFormat("Compiler: {0}", log);
        }

        if (!File.Exists(outputFilename))
        {
            throw new Exception("Compilation failed!");
        }

        //Remove unwanted .mdb file
        File.Delete(Path.Combine(outputFolder, assemblyName + ".dll.mdb"));

        Debug.Log("Script compilation complete.");
    }

    /// <summary>
    /// Change all non-Editor MonoScripts to reference the ASSEMBLY_NAME assembly, rather than the default Assembly-CSharp.
    /// </summary>
    protected void AdjustMonoScripts()
    {
        Debug.Log("Adjusting scripts...");

        IEnumerable<string> assetFolderPaths = AssetDatabase.GetAllAssetPaths().Where(path => path.EndsWith(".cs") && !path.StartsWith("Assets/Editor"));

        scriptPathsToRestore = new List<string>();

        foreach (var path in assetFolderPaths)
        {
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);

            if (script != null)
            {
                scriptPathsToRestore.Add(path);
                ChangeMonoScriptAssembly(script, assemblyName);
            }
        }
    }

    /// <summary>
    /// Restore the MonoScript references to point to Assembly-CSharp, which is expected by the Unity Editor.
    /// </summary>
    protected void RestoreMonoScripts()
    {
        Debug.Log("Restoring scripts...");

        foreach (var path in scriptPathsToRestore)
        {
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);

            if (script != null)
            {
                RestoreMonoScriptAssembly(script);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
        }
    }

    /// <summary>
    /// Make use of internal Unity functionality to change which assembly a MonoScript points to.
    /// 
    /// We change this to allow Unity to reconnect references to the script when loaded into KTaNE. Normally, a MonoScript
    /// points to the Assembly-CSharp.dll assembly. Because we are forced to build the mod assembly with a different name,
    /// Unity would not normally be able to reconnect the script. Here we can change the assembly name a MonoScript points to
    /// and resolve the problem.
    /// 
    /// WARNING! The Unity Editor expects MonoScripts to resolve to the Assembly-CSharp assembly, so you MUST change it back
    /// or else the editor will lose the script reference (and you'll be forced to delete your Library to recover).
    /// </summary>
    /// <param name="script"></param>
    /// <param name="assemblyName"></param>
    protected void ChangeMonoScriptAssembly(MonoScript script, string assemblyName)
    {
        //MonoScript
        //internal extern void Init(string scriptContents, string className, string nameSpace, string assemblyName, bool isEditorScript);
        MethodInfo dynMethod = script.GetType().GetMethod("Init", BindingFlags.NonPublic | BindingFlags.Instance);
        dynMethod.Invoke(script, new object[] { script.text, script.name, "", assemblyName, false });
        Debug.LogFormat("Changed {0} assembly to {1}", script.name, assemblyName);
    }

    protected void RestoreMonoScriptAssembly(MonoScript script)
    {
        ChangeMonoScriptAssembly(script, "Assembly-CSharp");
    }

    /// <summary>
    /// Copy all managed non-Editor assemblies to the OUTPUT_FOLDER for inclusion alongside the mod bundle.
    /// </summary>
    protected void CopyManagedAssemblies()
    {
        IEnumerable<string> assetPaths = AssetDatabase.GetAllAssetPaths().Where(path => path.EndsWith(".dll") && path.StartsWith("Assets/Plugins"));

        //Now find any other managed plugins that should be included, other than the EXCLUDED_ASSEMBLIES list
        foreach (string assetPath in assetPaths)
        {
            var pluginImporter = AssetImporter.GetAtPath(assetPath) as PluginImporter;

            if (pluginImporter != null && !pluginImporter.isNativePlugin && pluginImporter.GetCompatibleWithAnyPlatform())
            {
                string assetName = Path.GetFileName(assetPath);
                if (!EXCLUDED_ASSEMBLIES.Contains(assetName))
                {
                    string dest = Path.Combine(outputFolder, Path.GetFileName(assetPath));

                    Debug.LogFormat("Copying {0} to {1}", assetPath, dest);

                    File.Copy(assetPath, dest);
                }
            }
        }
    }

    /// <summary>
    /// Build the AssetBundle itself and copy it to the OUTPUT_FOLDER.
    /// </summary>
    protected void CreateAssetBundle()
    {
        Debug.Log("Building AssetBundle...");

        //Build all AssetBundles to the TEMP_BUILD_FOLDER
        if (!Directory.Exists(TEMP_BUILD_FOLDER))
        {
            Directory.CreateDirectory(TEMP_BUILD_FOLDER);
        }

        BuildPipeline.BuildAssetBundles(TEMP_BUILD_FOLDER, BuildAssetBundleOptions.DeterministicAssetBundle, BuildTarget.StandaloneWindows);

        //We are only interested in the BUNDLE_FILENAME bundle (and not the extra AssetBundle or the manifest files
        //that Unity makes), so just copy that to the final output folder
        string srcPath = Path.Combine(TEMP_BUILD_FOLDER, BUNDLE_FILENAME);
        string destPath = Path.Combine(outputFolder, BUNDLE_FILENAME);
        File.Copy(srcPath, destPath, true);
    }

    /// <summary>
    /// Creates a modInfo.json file and puts it in the OUTPUT_FOLDER.
    /// </summary>
    protected void CreateModInfo()
    {
        File.WriteAllText(outputFolder + "/modInfo.json", ModConfig.Instance.ToJson());
    }

    /// <summary>
    /// Print a warning for all non-Example assets that are not currently tagged to be in this AssetBundle.
    /// </summary>
    protected void WarnIfAssetsAreNotTagged()
    {
        string[] assetGUIDs = AssetDatabase.FindAssets("t:prefab,t:audioclip");

        foreach (var assetGUID in assetGUIDs)
        {
            string path = AssetDatabase.GUIDToAssetPath(assetGUID);

            if (!path.StartsWith("Assets/Examples"))
            {
                var importer = AssetImporter.GetAtPath(path);
                if (!importer.assetBundleName.Equals(BUNDLE_FILENAME))
                {
                    Debug.LogWarningFormat("Asset \"{0}\" is not tagged for {1} and will not be included in the AssetBundle!", path, BUNDLE_FILENAME);
                }
            }
        }

    }

    /// <summary>
    /// Verify that there is at least one thing to be included in the asset bundle.
    /// </summary>
    protected void CheckForAssets()
    {
        string[] assetsInBundle = AssetDatabase.FindAssets(string.Format("b:", BUNDLE_FILENAME));
        if (assetsInBundle.Length == 0)
        {
            throw new Exception(string.Format("No assets have been tagged for inclusion in the {0} AssetBundle.", BUNDLE_FILENAME));
        }
    }
}
