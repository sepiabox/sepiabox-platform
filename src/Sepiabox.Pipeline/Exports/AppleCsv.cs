using System.Text;

namespace Sepiabox.Pipeline.Exports;

/// <summary>
/// A small CSV reader for the files of an Apple data copy (S-047, D-109): comma separated, double quotes around fields
/// that hold commas, quotes (doubled) or line breaks, any line ending, an optional byte order mark. Lenient: it never throws
/// on odd input, an unclosed quote runs to the end of the text.
/// </summary>
public static class AppleCsv
{
    public static List<List<string>> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var started = false;
        var start = text.Length > 0 && text[0] == '﻿' ? 1 : 0;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"' when field.Length == 0:
                    quoted = true;
                    started = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    started = true;
                    break;
                case '\r' or '\n':
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    if (started || field.Length > 0 || row.Count > 0)
                    {
                        row.Add(field.ToString());
                        rows.Add(row);
                    }

                    row = [];
                    field.Clear();
                    started = false;
                    break;
                default:
                    field.Append(c);
                    started = true;
                    break;
            }
        }

        if (started || field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
