using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// Shared CSV helpers for the experiment configs and logs.
///
/// Two things this buys us:
///  1. Config files can carry a human readable header row. ReadRows() detects and
///     drops it, so the same reader works on files with and without a header.
///  2. All parsing is InvariantCulture, so a machine with a comma decimal
///     separator does not silently mangle "1.5" into 15.
/// </summary>
public static class CsvUtil
{
    public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// Reads a CSV into rows of cells. Blank lines, lines starting with '#' and a
    /// leading header row are skipped.
    /// </summary>
    public static List<string[]> ReadRows(string path)
    {
        var rows = new List<string[]>();

        if (!File.Exists(path))
        {
            Debug.LogError("CsvUtil: config file not found: " + path);
            return rows;
        }

        string[] lines = File.ReadAllLines(path);
        bool headerChecked = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrEmpty(line)) continue;

            string trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            if (trimmed[0] == '#') continue;

            if (!headerChecked)
            {
                headerChecked = true;
                if (LooksLikeHeader(trimmed)) continue;
            }

            rows.Add(SplitCells(trimmed));
        }

        return rows;
    }

    static string[] SplitCells(string line)
    {
        string[] cells = line.Split(',');
        for (int i = 0; i < cells.Length; i++)
            cells[i] = cells[i].Trim();
        return cells;
    }

    /// <summary>
    /// A row is treated as a header when its first cell is neither a number nor a
    /// bool. Every config in Data\Configs starts with a numeric column, so this is
    /// unambiguous for our files.
    /// </summary>
    static bool LooksLikeHeader(string line)
    {
        int comma = line.IndexOf(',');
        string first = (comma >= 0 ? line.Substring(0, comma) : line).Trim();

        if (first.Length == 0) return true;

        float f;
        if (float.TryParse(first, NumberStyles.Float, Inv, out f)) return false;

        bool b;
        if (bool.TryParse(first, out b)) return false;

        return true;
    }

    // ---- Safe cell accessors: missing / malformed columns fall back ------------

    public static float GetFloat(string[] row, int index, float fallback)
    {
        if (row == null || index < 0 || index >= row.Length) return fallback;
        float v;
        return float.TryParse(row[index], NumberStyles.Float, Inv, out v) ? v : fallback;
    }

    public static int GetInt(string[] row, int index, int fallback)
    {
        if (row == null || index < 0 || index >= row.Length) return fallback;
        int v;
        if (int.TryParse(row[index], NumberStyles.Integer, Inv, out v)) return v;

        // Tolerate "3.0" written into an int column.
        float f;
        if (float.TryParse(row[index], NumberStyles.Float, Inv, out f)) return Mathf.RoundToInt(f);

        return fallback;
    }

    public static bool GetBool(string[] row, int index, bool fallback)
    {
        if (row == null || index < 0 || index >= row.Length) return fallback;
        bool v;
        if (bool.TryParse(row[index], out v)) return v;

        // Tolerate 1/0 in a bool column.
        int i;
        if (int.TryParse(row[index], NumberStyles.Integer, Inv, out i)) return i != 0;

        return fallback;
    }

    public static bool HasColumn(string[] row, int index)
    {
        return row != null && index >= 0 && index < row.Length && row[index].Length > 0;
    }

    // ---- Log writing ----------------------------------------------------------

    /// <summary>
    /// Writes the header line if the log file does not exist yet or is empty.
    /// Call this right before the first AppendText of a run.
    /// </summary>
    public static void EnsureHeader(string path, string headerLine)
    {
        try
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (File.Exists(path) && new FileInfo(path).Length > 0) return;

            using (TextWriter w = File.CreateText(path))
                w.WriteLine(headerLine);
        }
        catch (Exception e)
        {
            Debug.LogError("CsvUtil.EnsureHeader failed for " + path + ": " + e.Message);
        }
    }

    /// <summary>Formats a float for a log cell with invariant culture.</summary>
    public static string F(float v) { return v.ToString(Inv); }
    public static string F(double v) { return v.ToString(Inv); }

    /// <summary>Quotes a cell if it contains a comma, quote or newline.</summary>
    public static string Cell(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.IndexOf(',') < 0 && s.IndexOf('"') < 0 && s.IndexOf('\n') < 0) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
