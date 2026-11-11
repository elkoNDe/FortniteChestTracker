namespace FortniteChestTracker;

/// <summary>Inspeccion limitada y en solo lectura de la cabecera del archivo de repeticion.</summary>
public static class ReplayProbe
{
    private const uint Magic = 0x1CA2E27F;

    /// <summary>
    /// Read the recorded replay length (milliseconds in the file header) without
    /// decoding its events. A missing/invalid length is unknown, never zero.
    /// This is replay duration, not necessarily the player's survival time.
    /// </summary>
    public static double? ReadDurationSeconds(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != Magic) return null;
            var version = reader.ReadUInt32();
            if (version >= 7)
            {
                var count = reader.ReadInt32();
                if (count < 0 || count > 64) return null;
                stream.Seek(count * 20L, SeekOrigin.Current);
            }
            var milliseconds = reader.ReadUInt32();
            // Guard against a zero length or a corrupt header.
            if (milliseconds == 0 || milliseconds > 6u * 60 * 60 * 1000) return null;
            return milliseconds / 1000.0;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (ArgumentException) { return null; }
    }

    public static (bool? IsLive, bool KeyMissing) Inspect(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != Magic) return (null, true);
            var version = reader.ReadUInt32();
            if (version >= 7)
            {
                var count = reader.ReadInt32();
                if (count < 0 || count > 64) return (null, true);
                stream.Seek(count * 20L, SeekOrigin.Current);
            }
            stream.Seek(12, SeekOrigin.Current);
            var len = reader.ReadInt32();
            if (len is < -16384 or > 32768) return (null, true);
            stream.Seek(len < 0 ? -2L * len : len, SeekOrigin.Current);
            var live = reader.ReadUInt32() != 0;
            _ = reader.ReadInt64(); // timestamp
            _ = reader.ReadUInt32(); // compressed
            var encrypted = reader.ReadUInt32() != 0;
            if (!encrypted) return (live, false);
            var keyLength = reader.ReadInt32();
            if (keyLength < 1 || keyLength > 256) return (live, true);
            var key = reader.ReadBytes(keyLength);
            return (live, key.Length != keyLength || key.All(b => b == 0));
        }
        catch (IOException) { return (null, true); }
        catch (UnauthorizedAccessException) { return (null, true); }
        catch (ArgumentException) { return (null, true); }
    }
}
