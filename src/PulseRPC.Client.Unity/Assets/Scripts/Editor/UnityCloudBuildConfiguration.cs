using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.Callbacks;

public static class UnityCloudBuildConfiguration
{
    /// <summary>
    /// UnityCloudbuild Post-Export method
    /// </summary>
    /// <param name="exportPath"></param>
    public static void PostBuild(string exportPath)
    {
        // package export
        PackageExporter.Export();
    }
}
