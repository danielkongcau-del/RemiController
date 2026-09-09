// RemiellePathPolicy —— 工作区路径三类法与写护栏（D1，2026-09-09）
// A 类：原始资产/历史证据输入（E:/ZZZ/local-only 冻结原件）——只读，任何写入必须被拒绝。
// B 类：当前工程输入——一律指向本工作副本（E:/ZZZ/ZCode/10_Unity/Remielle_Main）。
// C 类：一切输出（构建/截图/日志/验证报告）——必须落在 E:/ZZZ/ZCode/90_Builds 或 70_Automation/Agents/Reports。
// 既有脚本的输出常量已按此政策完成值级迁移（00_ProjectHub/AuditExports/path-audit-summary.md）；
// 新增工具必须经 GuardWrite 写文件。读根兼写根的混合根工具在 D1-c 拆分完成前不得新增。
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

        /// <summary>写前护栏：目标落入冻结原件（A 类）或工作区之外即抛异常拒绝。</summary>
        public static string GuardWrite(string path)
        {
            string full = Path.GetFullPath(path).Replace('\\', '/');
            if (full.StartsWith(FrozenOriginalRoot + "/", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(full.TrimEnd('/'), FrozenOriginalRoot, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("[RemiellePathPolicy] 禁止写入冻结原件（A 类只读）: " + full);
            if (!full.StartsWith(WorkspaceRoot + "/", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(full.TrimEnd('/'), WorkspaceRoot, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("[RemiellePathPolicy] 输出必须落在工作区内（C 类）: " + full);
            return full;
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
