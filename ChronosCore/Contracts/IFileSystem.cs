namespace Chronos.Core.Contracts;

public interface IFileSystem
{
    bool   Exists(string path);
    byte[] ReadAllBytes(string path);
    void   WriteAllBytes(string path, byte[] data);
}
