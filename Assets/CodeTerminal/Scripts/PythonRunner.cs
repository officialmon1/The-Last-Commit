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
    /// <summary>Runner error (Python not found, etc.), not an error in the user's code.</summary>
    public string error;
}

/// <summary>
/// Runs code with the local Python interpreter: write a temp file -> `python -I -u file.py` -> collect stdout/stderr, kill on timeout.
/// Interpreter lookup order: StreamingAssets/python/python.exe (drop in the official embeddable package for install-free builds) -> python / python3 / py on PATH.
/// Built-in library only, no sandbox: the player runs their own code.
/// </summary>
public static class PythonRunner
{
    const int OutputCap = 64 * 1024;
    static string _python;
    static string _tempDir;

    /// <summary>Call once on the main thread to cache Unity paths (Application.* cannot be used from background threads).</summary>
    public static void Init()
    {
        if (_tempDir == null) _tempDir = Application.temporaryCachePath;
        if (_python == null) _python = FindPython(Path.Combine(Application.streamingAssetsPath, "python", "python.exe"));
    }

    public static string PythonPath { get { Init(); return _python; } }

    public const string DownloadUrl = "https://www.python.org/downloads/";

    /// <summary>Install instructions shown to the player when Python is missing (per platform).</summary>
    public static string InstallHint =>
        Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor
            ? "Python 3 was not found on this Mac.\n" +
              "Install it, then restart the game:\n" +
              "  • Open Terminal and run:  xcode-select --install   (includes python3)\n" +
              "  • or download the installer from " + DownloadUrl
            : "Python 3 was not found on this computer.\n" +
              "Install it, then restart the game:\n" +
              "  • Download from " + DownloadUrl + "\n" +
              "  • In the installer tick \"Add python.exe to PATH\"\n" +
              "  • or run in a terminal:  winget install Python.Python.3.12";

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
            r.error = InstallHint;
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
                p.WaitForExit();   // let the async readers flush the remaining output
                r.exitCode = r.timedOut ? -1 : p.ExitCode;
            }
            r.runMs = sw.ElapsedMilliseconds;
            r.stdout = so.ToString();
            r.stderr = se.ToString();
            if (r.timedOut) r.stderr += $"\n[Timed out after {timeoutSeconds:0.#} s, process killed]\n";
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
