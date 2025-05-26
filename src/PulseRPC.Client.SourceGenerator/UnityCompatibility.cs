using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PulseRPC.Generator;

/// <summary>
/// Unity兼容性处理类，参考MemoryPack的做法
/// </summary>
internal static class UnityCompatibility
{
    /// <summary>
    /// 检测是否在Unity环境中编译
    /// </summary>
    public static bool IsUnityEnvironment(AnalyzerConfigOptionsProvider configProvider)
    {
        // 检查Unity相关的定义符号
        if (configProvider.GlobalOptions.TryGetValue("build_property.DefineConstants", out var defineConstants))
        {
            return defineConstants.Contains("UNITY_") ||
                   defineConstants.Contains("UNITY_EDITOR") ||
                   defineConstants.Contains("UNITY_2022") ||
                   defineConstants.Contains("UNITY_2023");
        }

        // 检查Target Framework是否为.NET Framework 4.x
        if (configProvider.GlobalOptions.TryGetValue("build_property.TargetFramework", out var targetFramework))
        {
            return targetFramework.StartsWith("net4") ||
                   targetFramework.Equals("net471") ||
                   targetFramework.Equals("net472") ||
                   targetFramework.Equals("net48");
        }

        // 检查项目名称或路径是否包含Unity标识
        if (configProvider.GlobalOptions.TryGetValue("build_property.MSBuildProjectName", out var projectName))
        {
            return projectName.IndexOf("Unity", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        return false;
    }

    /// <summary>
    /// 获取Unity兼容的代码生成选项
    /// </summary>
    public static UnityCodeGenOptions GetCodeGenOptions(AnalyzerConfigOptionsProvider configProvider)
    {
        var isUnity = IsUnityEnvironment(configProvider);

        return new UnityCodeGenOptions
        {
            IsUnityEnvironment = isUnity,
            UseUnityCompatibleSyntax = isUnity,
            AvoidModernCSharpFeatures = isUnity,
            UseSimpleGenerics = isUnity,
            GenerateUnityMetaFiles = false, // 不自动生成meta文件
            OutputDirectory = GetUnityOutputDirectory(configProvider)
        };
    }

    /// <summary>
    /// 生成Unity兼容的代码
    /// </summary>
    public static string GenerateUnityCompatibleCode(string originalCode, UnityCodeGenOptions options)
    {
        if (!options.IsUnityEnvironment)
            return originalCode;

        var code = originalCode;

        // 替换现代C#特性为Unity兼容版本
        if (options.AvoidModernCSharpFeatures)
        {
            // 移除scoped关键字
            code = code.Replace("scoped ", "");

            // 替换init-only properties为常规properties
            code = code.Replace("{ get; init; }", "{ get; set; }");

            // 简化泛型约束
            code = SimplifyGenericConstraints(code);

            // 移除nullable reference types注解
            code = RemoveNullableAnnotations(code);
        }

        return code;
    }

    /// <summary>
    /// 检查是否应该生成文件到磁盘
    /// </summary>
    private static bool ShouldGenerateFiles(AnalyzerConfigOptionsProvider configProvider)
    {
        // 检查用户配置
        if (configProvider.GlobalOptions.TryGetValue("build_property.PulseRPC_WriteFilesToDisk", out var writeFiles))
        {
            return writeFiles.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        // Unity环境默认启用文件生成
        return IsUnityEnvironment(configProvider);
    }

    /// <summary>
    /// 获取Unity输出目录
    /// </summary>
    private static string GetUnityOutputDirectory(AnalyzerConfigOptionsProvider configProvider)
    {
        // 用户指定目录
        if (configProvider.GlobalOptions.TryGetValue("build_property.PulseRPC_OutputFolder", out var outputFolder))
        {
            return outputFolder;
        }

        // 项目目录
        if (configProvider.GlobalOptions.TryGetValue("build_property.MSBuildProjectDirectory", out var projectDir))
        {
            // Unity项目的标准Generated目录
            return Path.Combine(projectDir, "Assets", "Scripts", "Generated");
        }

        return "Generated";
    }

    /// <summary>
    /// 简化泛型约束为Unity兼容版本
    /// </summary>
    private static string SimplifyGenericConstraints(string code)
    {
        // 移除复杂的泛型约束
        code = code.Replace("where TBufferWriter : class, IBufferWriter<byte>", "where TBufferWriter : IBufferWriter<byte>");
        return code;
    }

    /// <summary>
    /// 移除nullable reference types注解
    /// </summary>
    private static string RemoveNullableAnnotations(string code)
    {
        // 移除?注解，但保留nullable value types
        var lines = code.Split('\n');
        var result = new StringBuilder();

        foreach (var line in lines)
        {
            var processedLine = line;

            // 只移除reference types的nullable注解
            if (line.Contains("string?") && !line.Contains("string?[]"))
            {
                processedLine = line.Replace("string?", "string");
            }

            result.AppendLine(processedLine);
        }

        return result.ToString();
    }

    /// <summary>
    /// 创建Unity Meta文件
    /// </summary>
    public static void CreateUnityMetaFile(string filePath)
    {
        try
        {
            var metaPath = filePath + ".meta";
            if (File.Exists(metaPath))
                return;

            var guid = System.Guid.NewGuid().ToString("N");
            var metaContent = $@"fileFormatVersion: 2
guid: {guid}
MonoImporter:
  externalObjects: {{}}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {{instanceID: 0}}
  userData:
  assetBundleName:
  assetBundleVariant:
";
            File.WriteAllText(metaPath, metaContent);
        }
        catch
        {
            // 忽略Meta文件创建失败
        }
    }

    /// <summary>
    /// 生成Unity兼容的using语句
    /// </summary>
    public static string GenerateUnityCompatibleUsings()
    {
        return @"using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
#if UNITY_2021_3_OR_NEWER
using UnityEngine;
#endif
";
    }

    /// <summary>
    /// 检查是否需要添加Unity特定的条件编译
    /// </summary>
    public static string WrapWithUnityConditionalCompilation(string code, UnityCodeGenOptions options)
    {
        if (!options.IsUnityEnvironment)
            return code;

        return $@"#if UNITY_2021_3_OR_NEWER || NET471_OR_GREATER
{code}
#endif";
    }
}

/// <summary>
/// Unity代码生成选项
/// </summary>
internal class UnityCodeGenOptions
{
    public bool IsUnityEnvironment { get; set; }
    public bool UseUnityCompatibleSyntax { get; set; }
    public bool AvoidModernCSharpFeatures { get; set; }
    public bool UseSimpleGenerics { get; set; }
    public bool GenerateUnityMetaFiles { get; set; }
    public string OutputDirectory { get; set; } = "Generated";
}
