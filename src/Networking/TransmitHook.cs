using System.Runtime.InteropServices;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Memory;

namespace AntiWallHack;

internal sealed unsafe class TransmitHook : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void CheckTransmit(nint self, nint infos, int count, nint unionBits, nint unknownBits, nint networkables, nint indices, int entityCount);

    private readonly IUnmanagedFunction<CheckTransmit> function;
    private readonly Guid hook;

    public TransmitHook(ISwiftlyCore core, Action<nint, int> post, Action<Exception> failure)
    {
        nint table = core.Memory.GetVTableAddress("server", "CSource2GameEntities") ?? throw new InvalidOperationException("CSource2GameEntities vtable unavailable.");
        
        if (table == 0)
            throw new InvalidOperationException("Null transmit vtable.");

        int offset = core.GameData.GetOffset("ISource2GameEntities::CheckTransmit");
        function = core.Memory.GetUnmanagedFunctionByVTable<CheckTransmit>(table, offset);
        
        hook = function.AddHook(next => (self, infos, count, union, unknown, networkables, indices, entities) =>
        {
            next()(self, infos, count, union, unknown, networkables, indices, entities);
            try
            {
                post(infos, count);
            }
            catch (Exception error)
            {
                failure(error);
            }
        });
    }

    public void Dispose() => function.RemoveHook(hook);
}
