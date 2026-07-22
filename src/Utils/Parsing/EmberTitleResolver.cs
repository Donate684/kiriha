using System.IO;

namespace Kiriha.Utils.Parsing;

public static class EmberTitleResolver
{
    public static bool ScanFileForEmber(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[65536]; // Read first 64KB
            int bytesRead = fs.Read(buffer, 0, buffer.Length);

            // Search for "EMBER" (69, 77, 66, 69, 82)
            for (int i = 0; i < bytesRead - 4; i++)
            {
                if (buffer[i] == 'E' && buffer[i + 1] == 'M' && buffer[i + 2] == 'B' && buffer[i + 3] == 'E' && buffer[i + 4] == 'R')
                    return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public static string GetMeaningfulDirectoryName(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        while (!string.IsNullOrEmpty(dir))
        {
            string dirName = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(dirName)) break;

            string lower = dirName.ToLowerInvariant();
            if (lower == "series" || lower.StartsWith("season") || lower.StartsWith("s0") || lower == "ova" || lower == "ncop" || lower == "nced" || lower == "specials" || lower == "episodes")
            {
                dir = Path.GetDirectoryName(dir);
            }
            else
            {
                // Strip EMBER from the directory name so it doesn't get parsed as part of the anime title
                if (dirName.EndsWith("-EMBER", System.StringComparison.OrdinalIgnoreCase))
                    dirName = dirName.Substring(0, dirName.Length - 6).Trim();
                else if (dirName.EndsWith(" EMBER", System.StringComparison.OrdinalIgnoreCase))
                    dirName = dirName.Substring(0, dirName.Length - 6).Trim();
                else if (dirName.IndexOf("EMBER", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    dirName = System.Text.RegularExpressions.Regex.Replace(dirName, @"\bEMBER\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

                return dirName;
            }
        }
        return string.Empty;
    }
}
