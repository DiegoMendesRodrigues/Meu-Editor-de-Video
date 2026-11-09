using MeuEditorDeVideo.Core.AI;
using OpenCvSharp;

namespace MeuEditorDeVideo.AIEngine.Vision;

/// <summary>
/// Cache de máscaras de segmentação: memória (LRU) + disco (<c>cache\mattes\&lt;mídia&gt;\&lt;quadro&gt;.png</c>, tons de cinza).
/// A chave é o quadro de origem da mídia, de modo que pré-visualização (proxy) e exportação compartilham o resultado.
/// </summary>
public sealed class MatteCache(string? directory, int memoryCapacity = 120)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(Guid, long), LinkedListNode<((Guid, long) Key, Matte Matte)>> _map = [];
    private readonly LinkedList<((Guid, long) Key, Matte Matte)> _lru = new();

    public string? Directory { get; } = directory;

    public Matte? Get(Guid asset, long frame)
    {
        lock (_gate)
        {
            if (_map.TryGetValue((asset, frame), out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                return node.Value.Matte;
            }
        }
        if (FilePath(asset, frame) is not { } path || !File.Exists(path)) return null;
        try
        {
            using var mat = Cv2.ImDecode(File.ReadAllBytes(path), ImreadModes.Grayscale);
            if (mat.Empty()) return null;
            var matte = new Matte(mat.Width, mat.Height, CvImage.Bytes(mat));
            Remember(asset, frame, matte);
            return matte;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public bool Contains(Guid asset, long frame)
    {
        lock (_gate)
            if (_map.ContainsKey((asset, frame))) return true;
        return FilePath(asset, frame) is { } path && File.Exists(path);
    }

    public void Put(Guid asset, long frame, Matte matte, bool persist = true)
    {
        Remember(asset, frame, matte);
        if (!persist || FilePath(asset, frame) is not { } path) return;
        try
        {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var mat = CvImage.Gray(matte.Alpha, matte.Width, matte.Height);
            CvImage.WritePng(path, mat);
        }
        catch (IOException)
        {
            // Cache em disco é opcional: falta de espaço não impede a renderização.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Clear(Guid asset)
    {
        lock (_gate)
        {
            foreach (var key in _map.Keys.Where(k => k.Item1 == asset).ToList())
            {
                _lru.Remove(_map[key]);
                _map.Remove(key);
            }
        }
        if (Directory is null) return;
        var dir = Path.Combine(Directory, asset.ToString("N"));
        try
        {
            if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private void Remember(Guid asset, long frame, Matte matte)
    {
        lock (_gate)
        {
            if (_map.TryGetValue((asset, frame), out var existing)) _lru.Remove(existing);
            var node = _lru.AddFirst(((asset, frame), matte));
            _map[(asset, frame)] = node;
            while (_lru.Count > memoryCapacity)
            {
                var last = _lru.Last!;
                _lru.RemoveLast();
                _map.Remove(last.Value.Key);
            }
        }
    }

    private string? FilePath(Guid asset, long frame) =>
        Directory is null ? null : Path.Combine(Directory, asset.ToString("N"), frame.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".png");
}
