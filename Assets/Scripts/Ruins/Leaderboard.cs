using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Online leaderboard of the fastest escapes. The scores live in Neon Postgres behind a small Neon Function
/// (leaderboard-function/ in the repo); the game only talks to that function's public URL, so no database
/// credentials are needed in the build. GET returns the top entries, POST {name, seconds} adds one and returns its rank.
/// </summary>
public static class Leaderboard
{
    [Serializable] public class Entry { public string name; public int seconds; public string created_at; }
    [Serializable] public class Result { public List<Entry> entries = new List<Entry>(); public int rank; public string error; }
    [Serializable] class Submission { public string name; public int seconds; }

    /// <summary>Public URL of the deployed leaderboard function (no secret: the function holds the database access).</summary>
    public const string BaseUrl = "https://br-small-boat-b5hr9wyk-leaderboard.compute.c-7.us-east-2.aws.neon.tech/";
    public const int MaxNameLength = 20;
    const int TimeoutSeconds = 10;

    public static void Fetch(MonoBehaviour host, Action<Result> onDone) => host.StartCoroutine(Request(UnityWebRequest.Get(BaseUrl), onDone));

    public static void Submit(MonoBehaviour host, string name, int seconds, Action<Result> onDone)
    {
        string body = JsonUtility.ToJson(new Submission { name = name, seconds = seconds });
        var req = new UnityWebRequest(BaseUrl, "POST") { uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body)), downloadHandler = new DownloadHandlerBuffer() };
        req.SetRequestHeader("Content-Type", "application/json");
        host.StartCoroutine(Request(req, onDone));
    }

    static IEnumerator Request(UnityWebRequest req, Action<Result> onDone)
    {
        using (req)
        {
            req.timeout = TimeoutSeconds;
            yield return req.SendWebRequest();
            Result result;
            if (req.result != UnityWebRequest.Result.Success)
                result = new Result { error = req.responseCode > 0 ? $"HTTP {req.responseCode}: {req.downloadHandler.text}" : req.error };
            else
            {
                try { result = JsonUtility.FromJson<Result>(req.downloadHandler.text) ?? new Result { error = "empty reply" }; }
                catch (Exception e) { result = new Result { error = "bad reply: " + e.Message }; }
            }
            onDone?.Invoke(result);
        }
    }

    public static string FormatTime(int seconds) => $"{seconds / 60:00}:{seconds % 60:00}";
}
