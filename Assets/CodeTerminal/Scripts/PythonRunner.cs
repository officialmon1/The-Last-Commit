using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public class PythonResult
{
    public string stdout = "";
    public string stderr = "";
    public int exitCode;
    public bool timedOut;
    public long runMs;
    /// <summary>运行器本身的错误（找不到 Python 等），不是用户代码的错误。</summary>
    public string error;
}

/// <summary>
/// 用本机的 Python 解释器跑一段代码：写到临时文件 → `python -I -u file.py` → 收集 stdout/stderr，超时就杀掉。
/// 解释器查找顺序：StreamingAssets/python/python.exe（可放 Python 官方 embeddable 包，打包后免安装）→ PATH 里的 python / python3 / py。
/// 只用内置库，不做沙箱；玩家跑的是自己写的代码。
/// </summary>
public static class PythonRunner
{
    const int OutputCap = 64 * 1024;
    static string _python;
    static string _tempDir;

    /// <summary>在主线程调一次，缓存 Unity 路径（Application.* 不能在后台线程用）。</summary>
    public static void Init()
    {
        if (_tempDir == null) _tempDir = Application.temporaryCachePath;
        if (_python == null) _python = FindPython(Path.Combine(Application.streamingAssetsPath, "python", "python.exe"));
    }

    public static string PythonPath { get { Init(); return _python; } }

    public static Task<PythonResult> RunAsync(string code, string stdin, float timeoutSeconds)
    {
        Init();
        string python = _python, tempDir = _tempDir;
        return Task.Run(() => Run(python, tempDir, code, stdin ?? "", timeoutSeconds));
    }

    static PythonResult Run(string python, string tempDir, string code, string stdin, float timeoutSeconds)
    {
        var r = new PythonResult();
        if (string.IsNullOrEmpty(python))
        {
            r.error = "找不到 Python：请安装 Python 3 并加入 PATH，或把 embeddable 包解压到 StreamingAssets/python/";
            return r;
        }
        string file = Path.Combine(tempDir, "run_" + Guid.NewGuid().ToString("N") + ".py");
        try
        {
            File.WriteAllText(file, code, new UTF8Encoding(false));
            var psi = new ProcessStartInfo
            {
                FileName = python,
                Arguments = "-I -u \"" + file + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = tempDir,
            };
            psi.Environment["PYTHONIOENCODING"] = "utf-8";
            var so = new StringBuilder();
            var se = new StringBuilder();
            var sw = Stopwatch.StartNew();
            using (var p = new Process { StartInfo = psi })
            {
                p.OutputDataReceived += (_, e) => { if (e.Data != null && so.Length < OutputCap) so.Append(e.Data).Append('\n'); };
                p.ErrorDataReceived += (_, e) => { if (e.Data != null && se.Length < OutputCap) se.Append(e.Data).Append('\n'); };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                try { p.StandardInput.Write(stdin); p.StandardInput.Close(); } catch (IOException) { }
                if (!p.WaitForExit((int)(timeoutSeconds * 1000)))
                {
                    r.timedOut = true;
                    try { p.Kill(); } catch { }
                }
                p.WaitForExit();   // 等异步读取把剩余输出刷完
                r.exitCode = r.timedOut ? -1 : p.ExitCode;
            }
            r.runMs = sw.ElapsedMilliseconds;
            r.stdout = so.ToString();
            r.stderr = se.ToString();
            if (r.timedOut) r.stderr += $"\n[超时：超过 {timeoutSeconds:0.#} 秒，已终止]\n";
        }
        catch (Exception e)
        {
            r.error = e.Message;
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
        return r;
    }

    static string FindPython(string bundled)
    {
        if (File.Exists(bundled)) return bundled;
        foreach (var c in new[] { "python", "python3", "py" })
        {
            try
            {
                var psi = new ProcessStartInfo(c, "--version") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using var p = Process.Start(psi);
                string v = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                if (p.WaitForExit(5000) && p.ExitCode == 0 && v.Contains("Python 3")) return c;
            }
            catch { }
        }
        return null;
    }
}
