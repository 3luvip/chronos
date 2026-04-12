using Chronos.Core.Contracts;
using Godot;

namespace Chronos.Client.Adapters;

public sealed class GodotFileSystem : IFileSystem
{
    public bool Exists(string path) => FileAccess.FileExists(path);

    public byte[] ReadAllBytes(string path)
    {
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        return f.GetBuffer((long)f.GetLength());
    }

    public void WriteAllBytes(string path, byte[] data)
    {
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        f.StoreBuffer(data);
    }
}
