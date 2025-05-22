using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PulseRPC.Editor
{
    /// <summary>
    /// 用于从命令行构建 UnityPackage
    /// </summary>
    public static class BuildPackage
    {
        /// <summary>
        /// 从命令行调用此方法来构建 UnityPackage
        /// 使用方法：Unity.exe -batchmode -quit -executeMethod PulseRPC.Editor.BuildPackage.Build
        /// </summary>
        public static void Build()
        {
            Debug.Log("开始构建 PulseRPC Unity 包...");

            try
            {
                // 导出 UnityPackage
                PackageExporter.Export();

                Debug.Log("PulseRPC Unity 包构建成功");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"构建失败: {ex.Message}\n{ex.StackTrace}");
                EditorApplication.Exit(1);
            }

            EditorApplication.Exit(0);
        }
    }
}
