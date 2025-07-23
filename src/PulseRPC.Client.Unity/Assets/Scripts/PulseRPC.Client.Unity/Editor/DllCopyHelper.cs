using System.IO;
using UnityEditor;
using UnityEngine;

namespace PulseRPC.Client.Unity.Editor
{
    public static class DllCopyHelper
    {
        private static readonly string[] RequiredDlls = {
            "PulseRPC.Abstractions.dll",
            "PulseRPC.Client.dll",
            "PulseRPC.Shared.dll",
            "PulseRPC.Client.SourceGenerator.dll"
        };

        [MenuItem("PulseRPC/Copy Required DLLs")]
        public static void CopyRequiredDlls()
        {
            var pluginsPath = Path.Combine(Application.dataPath, "Scripts", "PulseRPC.Client.Unity", "Plugins");
            
            if (!Directory.Exists(pluginsPath))
            {
                Directory.CreateDirectory(pluginsPath);
            }

            // 从解决方案的构建输出目录复制 DLL
            var solutionRoot = GetSolutionRoot();
            if (string.IsNullOrEmpty(solutionRoot))
            {
                Debug.LogError("无法找到解决方案根目录");
                return;
            }

            foreach (var dll in RequiredDlls)
            {
                var sourcePath = FindDllInBuildOutput(solutionRoot, dll);
                if (!string.IsNullOrEmpty(sourcePath))
                {
                    var destPath = Path.Combine(pluginsPath, dll);
                    File.Copy(sourcePath, destPath, true);
                    Debug.Log($"已复制 {dll} 到 {destPath}");
                }
                else
                {
                    Debug.LogWarning($"找不到 {dll}");
                }
            }

            AssetDatabase.Refresh();
        }

        private static string GetSolutionRoot()
        {
            var currentPath = Application.dataPath;
            while (!string.IsNullOrEmpty(currentPath))
            {
                if (File.Exists(Path.Combine(currentPath, "PulseRPC.sln")))
                {
                    return currentPath;
                }
                currentPath = Directory.GetParent(currentPath)?.FullName;
            }
            return null;
        }

        private static string FindDllInBuildOutput(string solutionRoot, string dllName)
        {
            var searchPaths = new[]
            {
                Path.Combine(solutionRoot, "src", dllName.Replace(".dll", ""), "bin", "Release"),
                Path.Combine(solutionRoot, "src", dllName.Replace(".dll", ""), "bin", "Debug"),
                Path.Combine(solutionRoot, "bin", "Release"),
                Path.Combine(solutionRoot, "bin", "Debug")
            };

            foreach (var searchPath in searchPaths)
            {
                if (Directory.Exists(searchPath))
                {
                    var files = Directory.GetFiles(searchPath, dllName, SearchOption.AllDirectories);
                    if (files.Length > 0)
                    {
                        return files[0];
                    }
                }
            }

            return null;
        }
    }
}