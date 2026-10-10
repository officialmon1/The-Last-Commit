using System;

namespace Panda
{
    [Serializable]
    public class Offer
    {
        public string state;
        public string kind;
        public string text;
        public string yes_text;
        public long id;
    }

    [Serializable]
    public class StatusResponse
    {
        public bool face_on;
        public string state;
        public string player_state;
        public bool calibrating;
        public bool present;
        public Offer offer;
        public int failed_runs;
        public string why;
    }

    [Serializable]
    public class HintResponse
    {
        public string text;
        public string mood;
        public string player_state;
    }

    [Serializable]
    public class EventResponse
    {
        public int failed_runs;
        public string celebrate;
        public string text;
    }

    [Serializable]
    public class FaceStartResponse
    {
        public bool ok;
        public string error;
    }

    public static class Json
    {
        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append(' ');
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        public static string StringArray(System.Collections.Generic.IEnumerable<string> values)
        {
            var sb = new System.Text.StringBuilder("[");
            bool first = true;
            foreach (var v in values)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(Escape(v)).Append('"');
            }
            sb.Append(']');
            return sb.ToString();
        }
    }
}
