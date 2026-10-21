using System.Collections.Concurrent;

namespace AntiWallHack;

// Engine state and publication stay on the frame thread. The worker only reads files.
internal sealed class VisibilityLoader : IDisposable
{
    private readonly string root;
    private readonly string mapsDirectory;
    private readonly Action<string> log;
    private readonly bool debug;
    private readonly Func<string, string, string, CancellationToken, Action<string>, VisibilityMap> load;
    private readonly ConcurrentQueue<string> progress = new();
    private CancellationTokenSource? cancellation;
    private Task<VisibilityMap>? pending;
    private long generation;
    private long loadingGeneration;
    private long nextPoll;
    private long nextNotice;
    private (bool Exists, long Size, DateTime Time)? observed;
    private string map = "";
    private string loadingPath = "";
    private string status = "waiting for map physics before VIS validation";
    public VisibilityMap? Current { get; private set; }

    public VisibilityLoader(string root, string mapsDirectory, Action<string> log, bool debug,
        Func<string, string, string, CancellationToken, Action<string>, VisibilityMap>? load = null)
    {
        this.root = Path.GetFullPath(root);
        this.mapsDirectory = Path.GetFullPath(mapsDirectory);
        this.log = log;
        this.debug = debug;
        this.load = load ?? VisibilityMap.Load;
    }

    public void ChangeMap(string identity)
    {
        cancellation?.Cancel();
        generation++;
        map = identity;
        Current = null;
        observed = null;
        nextPoll = 0;
        nextNotice = 0;
        status = "waiting for map physics before VIS validation";
    }

    public void Tick(bool physicsReady, long now)
    {
        while (progress.TryDequeue(out var message))
        {
            if (debug)
            {
                log("[AntiWallHack][Debug] " + message);
            }
        }

        if (pending is { IsCompleted: true })
        {
            try
            {
                var loaded = pending.GetAwaiter().GetResult();

                if (loadingGeneration == generation)
                {
                    var file = new FileInfo(loadingPath);
                    var stamp = (file.Exists, file.Exists ? file.Length : 0, file.Exists ? file.LastWriteTimeUtc : default);

                    if (observed != stamp)
                    {
                        throw new InvalidDataException("VIS table changed during validation; retrying");
                    }

                    Current = loaded;
                    nextNotice = 0;

                    log($"[AntiWallHack] VIS activated for {map}: {loaded.Count} cells, all enemies eligible.");
                }
            }
            catch (Exception error)
            {
                if (loadingGeneration == generation)
                {
                    Current = null;
                    status = "VIS validation failed: " + error.Message;
                    observed = null;
                    nextPoll = now + 30000;
                }
            }

            pending = null;
            cancellation?.Dispose();
            cancellation = null;
        }

        if (physicsReady && pending == null && map.Length != 0 && now >= nextPoll)
        {
            nextPoll = now + 5000;

            try
            {
                if (MapIdentity.Resolve(map) != map)
                {
                    throw new InvalidDataException("Invalid current map identity");
                }

                string path = Path.Combine(mapsDirectory, map + ".awvis");
                var file = new FileInfo(path);
                var stamp = (file.Exists, file.Exists ? file.Length : 0, file.Exists ? file.LastWriteTimeUtc : default);

                if (observed != stamp)
                {
                    observed = stamp;
                    Current = null;

                    if (!file.Exists)
                    {
                        status = "baked VIS table missing: " + path;
                    }
                    else
                    {
                        status = "validating baked VIS table: " + path;
                        loadingGeneration = generation;
                        loadingPath = path;
                        cancellation = new CancellationTokenSource();
                        var token = cancellation.Token;
                        string identity = map;
                        
                        pending = Task.Factory.StartNew(() =>
                        {
                            try
                            {
                                Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                            }
                            catch (Exception error)
                            {
                                progress.Enqueue("Worker priority unchanged; reads remain throttled: " + error.Message);
                            }

                            return load(path, root, identity, token, message => progress.Enqueue(message));
                            
                        }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                    }
                }
            }
            catch (Exception error)
            {
                Current = null;
                status = "VIS unavailable: " + error.Message;
            }
        }
        if (Current == null && map.Length != 0 && now >= nextNotice)
        {
            log($"[AntiWallHack] Checks disabled for {map}: {status}. Will resume automatically when VIS is ready.");
            nextNotice = now + 300000;
        }
    }

    public void Dispose()
    {
        cancellation?.Cancel();
        
        try
        {
            pending?.GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // A cancelled or failed file read must still finish before unloading.
        }

        cancellation?.Dispose();
        cancellation = null;
        pending = null;
        Current = null;
    }
}
