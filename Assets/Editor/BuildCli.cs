using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SpriteBakerDemo.BuildTools
{
    /// <summary>
    /// Batchmode entry point paired with <c>Tools/Build/Build-Demo.ps1</c>.
    /// Reads <c>-cliBuildPath</c> + <c>-cliReportPath</c>, re-asserts
    /// PlayerSettings invariants, builds WebGL, writes a JSON report,
    /// exits 0/1.
    /// </summary>
    public static class BuildCli
    {
        const string SCENE_PATH = "Assets/Demo/Scenes/SpriteBakerDemo.unity";

        // -executeMethod SpriteBakerDemo.BuildTools.BuildCli.BuildWebGL
        public static void BuildWebGL()
        {
            var report = new BuildReportData();
            try
            {
                var args = ParseArgs();
                string buildDir   = args.Get("-cliBuildPath",   "build/WebGL");
                string reportPath = args.Get("-cliReportPath",  "Tools/Build/output/report-BuildWebGL.json");

                Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
                report.reportPath = reportPath;

                // Re-assert at build time so a hand-edit of
                // ProjectSettings.asset can't regress these.
                PlayerSettings.WebGL.compressionFormat     = WebGLCompressionFormat.Brotli;
                PlayerSettings.WebGL.decompressionFallback = true;
                PlayerSettings.WebGL.template              = "PROJECT:SpriteBakerDemo";

                // Surface stack traces in the browser console for Error /
                // Exception logs. WebGL Brotli builds default to "None"
                // which prints a bare "NullReferenceException" with no
                // location — useless for diagnosing runtime issues. Script-
                // only traces add a few KB to the build's IL2CPP metadata.
                PlayerSettings.SetStackTraceLogType(LogType.Error,     StackTraceLogType.ScriptOnly);
                PlayerSettings.SetStackTraceLogType(LogType.Exception, StackTraceLogType.ScriptOnly);
                PlayerSettings.SetStackTraceLogType(LogType.Assert,    StackTraceLogType.ScriptOnly);

                var opts = new BuildPlayerOptions
                {
                    scenes           = new[] { SCENE_PATH },
                    locationPathName = buildDir,
                    target           = BuildTarget.WebGL,
                    targetGroup      = BuildTargetGroup.WebGL,
                    options          = BuildOptions.None,
                };

                Debug.Log($"[BuildCli] Building WebGL → {buildDir}");
                BuildReport result = BuildPipeline.BuildPlayer(opts);

                report.success     = result.summary.result == BuildResult.Succeeded;
                report.message     = result.summary.result.ToString();
                report.sizeBytes   = (long)result.summary.totalSize;
                report.durationSec = (float)result.summary.totalTime.TotalSeconds;
                report.indexPath   = Path.Combine(buildDir, "index.html");

                if (!report.success)
                {
                    foreach (var step in result.steps)
                    foreach (var msg in step.messages)
                    {
                        if (msg.type == LogType.Error || msg.type == LogType.Exception)
                            report.message += "\n  " + msg.content;
                    }
                }
            }
            catch (Exception ex)
            {
                report.success = false;
                report.message = "Exception: " + ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace;
            }
            finally
            {
                WriteReport(report);
                EditorApplication.Exit(report.success ? 0 : 1);
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        [Serializable]
        struct BuildReportData
        {
            public bool   success;
            public string message;
            public long   sizeBytes;
            public float  durationSec;
            public string indexPath;
            public string reportPath; // internal — not written to JSON
        }

        static void WriteReport(BuildReportData data)
        {
            try
            {
                if (string.IsNullOrEmpty(data.reportPath)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(data.reportPath));
                // Stable shape so PowerShell ConvertFrom-Json reads every
                // field even on early abort.
                var json = $"{{\n" +
                           $"  \"success\": {(data.success ? "true" : "false")},\n" +
                           $"  \"message\": \"{Escape(data.message)}\",\n" +
                           $"  \"sizeBytes\": {data.sizeBytes},\n" +
                           $"  \"durationSec\": {data.durationSec.ToString(System.Globalization.CultureInfo.InvariantCulture)},\n" +
                           $"  \"indexPath\": \"{Escape(data.indexPath)}\"\n" +
                           $"}}\n";
                File.WriteAllText(data.reportPath, json);
            }
            catch (Exception ex)
            {
                Debug.LogError("[BuildCli] Failed to write report: " + ex.Message);
            }
        }

        static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        }

        class CliArgs
        {
            readonly string[] _argv;
            public CliArgs(string[] argv) { _argv = argv; }
            public string Get(string name, string fallback)
            {
                for (int i = 0; i < _argv.Length - 1; i++)
                    if (_argv[i] == name) return _argv[i + 1];
                return fallback;
            }
        }

        static CliArgs ParseArgs() => new CliArgs(Environment.GetCommandLineArgs());
    }
}
