// RemiellePathPolicy —— 工作区路径三类法与写护栏（D1，2026-09-09；v2 增只读根登记）
// A 类：原始资产/历史证据输入（local-only 冻结原件）与工作区内五组资产副本——只读，任何写入必须被拒绝。
// B 类：当前工程输入/编辑——仅限 E:/ZZZ/ZCode/10_Unity/Remielle_Main 工程目录与 50_AssetPipeline/60_Experiments 工作区。
// C 类：一切输出（构建/截图/日志/验证报告）——必须落在 E:/ZZZ/ZCode/90_Builds 或 70_Automation/Agents/Reports。
// 新增工具必须经 GuardWrite 写文件；旧脚本直写不受保护——因此 D1-c 要求所有写调用点迁移到护栏路径。
using System;
using System.IO;

namespace Remielle
{
    public static class RemiellePathPolicy
    {
        public const string WorkspaceRoot = "E:/ZZZ/ZCode";
        public const string FrozenOriginalRoot = "E:/ZZZ/local-only";
        public const string ProjectRoot = WorkspaceRoot + "/10_Unity/Remielle_Main";
        public const string BuildOutputRoot = WorkspaceRoot + "/90_Builds";
        public const string ReportRoot = WorkspaceRoot + "/70_Automation/Agents/Reports";

        /// <summary>A 类只读根：冻结原件 + 工作区内五组资产副本（数据身份完整性）。</summary>
        public static readonly string[] ReadOnlyRoots =
        {
            FrozenOriginalRoot,
            WorkspaceRoot + "/20_ReverseEngineering/AssetVault",
            WorkspaceRoot + "/20_ReverseEngineering/RuntimeRepair",
            WorkspaceRoot + "/20_ReverseEngineering/DataAcquisition",
            WorkspaceRoot + "/20_ReverseEngineering/RenderingReview",
            WorkspaceRoot + "/20_ReverseEngineering/Evidence",
            WorkspaceRoot + "/20_ReverseEngineering/Manifests",
        };

        /// <summary>B 类可写工程根：主工程（开发）与管线/实验工作区。</summary>
        public static readonly string[] WritableProjectRoots =
        {
            ProjectRoot,
            WorkspaceRoot + "/50_AssetPipeline",
            WorkspaceRoot + "/60_Experiments",
        };

        static bool Under(string path, string root)
        {
            return path.StartsWith(root.Replace('\\', '/') + "/", StringComparison.OrdinalIgnoreCase)
                || string.Equals(path.TrimEnd('/'), root.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>写前护栏（C 类输出）：拒绝写入任何只读根（冻结原件与资产副本），也拒绝工作区之外。</summary>
        public static string GuardWrite(string path)
        {
            string full = Path.GetFullPath(path).Replace('\\', '/');
            foreach (var root in ReadOnlyRoots)
                if (Under(full, root))
                    throw new UnauthorizedAccessException("[RemiellePathPolicy] 禁止写入只读根（A 类）: " + full + "（root=" + root + "）");
            if (!Under(full, WorkspaceRoot))
                throw new UnauthorizedAccessException("[RemiellePathPolicy] 输出必须落在工作区内: " + full);
            return full;
        }

        /// <summary>写前护栏（B 类工程编辑）：仅允许写入主工程/管线/实验目录；资产副本仍拒绝。</summary>
        public static string GuardProjectEdit(string path)
        {
            string full = Path.GetFullPath(path).Replace('\\', '/');
            foreach (var root in ReadOnlyRoots)
                if (Under(full, root))
                    throw new UnauthorizedAccessException("[RemiellePathPolicy] 禁止写入只读根（A 类）: " + full);
            foreach (var root in WritableProjectRoots)
                if (Under(full, root))
                    return full;
            throw new UnauthorizedAccessException("[RemiellePathPolicy] 非法工程编辑目标（仅限主工程/管线/实验区）: " + full);
        }

        /// <summary>取 C 类输出目录（自动创建）。domain 如 "ControllerImplementation/20260906"。</summary>
        public static string BuildOutputFor(string domain)
        {
            var d = BuildOutputRoot + "/" + domain.Trim('/') + "/";
            Directory.CreateDirectory(GuardWrite(d));
            return d;
        }
    }
}
