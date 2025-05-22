using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PulseRPC.Editor
{
    /// <summary>
    /// PulseRPC 包导出工具
    /// </summary>
    public static class PackageExporter
    {
        private const string PackageName = "PulseRPC.Client.Unity";
        private static readonly string[] TargetDirectories = new[]
        {
            "Assets/Scripts/PulseRPC.Client.Unity"
        };

        /// <summary>
        /// 导出 UnityPackage
        /// </summary>
        [MenuItem("PulseRPC/Export UnityPackage")]
        public static void Export()
        {
            var version = GetVersion();
            var fileName = $"{PackageName}.{version}.unitypackage";
            var exportPath = Path.Combine(GetProjectRoot(), fileName);

            try
            {
                // 获取要导出的资源路径
                var exportingAssets = TargetDirectories
                    .SelectMany(dir => Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    .Where(path => !path.EndsWith(".meta") && !path.EndsWith(".DS_Store") && !path.Contains("/."))
                    .Select(path => path.Replace("\\", "/"))
                    .ToArray();

                // 导出 UnityPackage
                AssetDatabase.ExportPackage(exportingAssets, exportPath, ExportPackageOptions.Default);
                Debug.Log($"导出成功: {exportPath}");

                // 打开文件夹
                EditorUtility.RevealInFinder(exportPath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"导出失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取项目根目录
        /// </summary>
        private static string GetProjectRoot()
        {
            var projectDir = Path.GetDirectoryName(Application.dataPath);
            return projectDir;
        }

        /// <summary>
        /// 获取当前版本号
        /// </summary>
        private static string GetVersion()
        {
            var packageJsonPath = Path.Combine(TargetDirectories[0], "package.json");
            if (!File.Exists(packageJsonPath))
                return "1.0.0";

            try
            {
                var json = File.ReadAllText(packageJsonPath);
                var versionMatch = System.Text.RegularExpressions.Regex.Match(json, "\"version\"\\s*:\\s*\"(.+?)\"");
                if (versionMatch.Success)
                    return versionMatch.Groups[1].Value;
            }
            catch (Exception ex)
            {
                Debug.LogError($"读取版本号失败: {ex.Message}");
            }

            return "1.0.0";
        }
    }
}
