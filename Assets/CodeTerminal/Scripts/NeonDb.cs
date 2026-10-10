using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Neon Postgres 客户端：走 Neon 的 HTTP SQL 接口（POST https://&lt;host&gt;/sql），不需要任何数据库 DLL。
/// 连接串放在 StreamingAssets/neon.json（已 gitignore），格式见 neon.example.json。
/// 查询用 row_to_json 把每行变成一个 JSON 字符串，再用 JsonUtility 解析成 [Serializable] 类（字段名 = 列名）。
/// </summary>
public static class NeonDb
{
    [Serializable] class Config { public string connectionString; }
    [Serializable] class Row { public string j; }
    [Serializable] class Response { public Row[] rows; public string message; }

    static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    static string _conn, _url;
    static bool _loaded;

    public static bool Available { get { Load(); return _url != null; } }

    static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            var path = Path.Combine(Application.streamingAssetsPath, "neon.json");
            if (!File.Exists(path)) { Debug.LogWarning("NeonDb: 没有 StreamingAssets/neon.json，数据库功能关闭（复制 neon.example.json 填入连接串）。"); return; }
            _conn = JsonUtility.FromJson<Config>(File.ReadAllText(path)).connectionString;
            var uri = new Uri(_conn.Replace("postgresql://", "https://").Replace("postgres://", "https://"));
            _url = "https://" + uri.Host + "/sql";
        }
        catch (Exception e) { Debug.LogWarning("NeonDb: 读取 neon.json 失败：" + e.Message); }
    }

    /// <summary>
    /// 执行 SQL。要拿结果时，SELECT 里把每行包成 json 文本并命名为 j，例如
    /// <c>select row_to_json(p)::text as j from problems p where slug = $1</c>，返回的每个字符串可用 JsonUtility.FromJson 解析。
    /// </summary>
    public static async Task<List<string>> Query(string sql, params string[] args)
    {
        Load();
        if (_url == null) throw new InvalidOperationException("NeonDb 未配置");
        var body = new StringBuilder("{\"query\":").Append(Json(sql)).Append(",\"params\":[");
        for (int i = 0; i < args.Length; i++) { if (i > 0) body.Append(','); body.Append(args[i] == null ? "null" : Json(args[i])); }
        body.Append("]}");
        using var req = new HttpRequestMessage(HttpMethod.Post, _url) { Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json") };
        req.Headers.Add("Neon-Connection-String", _conn);
        using var res = await Http.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode) throw new Exception($"Neon {(int)res.StatusCode}: {text}");
        var parsed = JsonUtility.FromJson<Response>(text);
        var list = new List<string>();
        if (parsed?.rows != null) foreach (var r in parsed.rows) if (r.j != null) list.Add(r.j);
        return list;
    }

    public static Task Exec(string sql, params string[] args) => Query(sql, args);

    static string Json(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}
