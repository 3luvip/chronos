using System.Collections.Generic;
using System.IO;
using Chronos.Core.Contracts;

namespace Chronos.Core.Tests.Doubles;

public sealed class MemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, byte[]> _files = new();

    public void AddFile(string path, byte[] data) => _files[path] = data;

    public bool   Exists(string path)            => _files.ContainsKey(path);
    public byte[] ReadAllBytes(string path)      =>
        _files.TryGetValue(path, out var d) ? d : throw new FileNotFoundException(path);
    public void   WriteAllBytes(string path, byte[] data) => _files[path] = data;
}
