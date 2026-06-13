using System.IO;

namespace Excalibur5.Config;

/// <summary>
/// Helpers for crash-safe file writes. Content is written to a sibling temp file
/// first and then moved into place, so a crash mid-write leaves either the old
/// complete file or the new complete file — never a truncated/corrupt one.
/// </summary>
internal static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, contents);
        File.Move(tmp, path, overwrite: true);
    }

    public static void WriteAllBytes(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, overwrite: true);
    }
}
