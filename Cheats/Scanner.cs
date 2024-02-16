using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using System.Text;
using Common;

namespace Cheats;

internal sealed class MemoryScanner : IDisposable
{
    [DllImport("kernel32.dll")]
    private static extern int VirtualQueryEx(nint hProcess, nint lpAddress, out MemoryBasicInformation64 lpBuffer, uint dwLength);

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct MemoryBasicInformation64
    {
        public readonly long BaseAddress;
        public readonly long AllocationBase;
        public readonly int AllocationProtect;
        public readonly int __pad0;
        public readonly long RegionSize;
        public readonly int State;
        public readonly int Protect;
        public readonly int Type;
        public readonly int __pad1;
    }

    [Flags]
    private enum ProcessAccessFlags : uint
    {
        All = 0x001F0FFF,
        Terminate = 0x00000001,
        CreateThread = 0x00000002,
        VMOperation = 0x00000008,
        VMRead = 0x00000010,
        VMWrite = 0x00000020,
        DupHandle = 0x00000040,
        SetInformation = 0x00000200,
        QueryInformation = 0x00000400,
        Synchronize = 0x00100000
    }

    private const ProcessAccessFlags ProcessRead = ProcessAccessFlags.VMRead | ProcessAccessFlags.VMOperation |
                                            ProcessAccessFlags.QueryInformation;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(nint hProcess, nint lpBaseAddress, [Out] byte[] lpBuffer, nint dwSize, out nint lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(nint hProcess, nint lpBaseAddress, byte[] lpBuffer, uint nSize, out nuint lpNumberOfBytesWritten);

    [DllImport("kernel32.dll")]
    private static extern nint OpenProcess(ProcessAccessFlags dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern int CloseHandle(nint hProcess);

    public nint Handle { get; }

    public Process Process { get; }

    public MemoryScanner(Process process)
    {
        Process = process;
        Handle = OpenProcess(ProcessRead, false, process.Id);
    }

    public List<MemoryBasicInformation64> MapMemoryRegions()
    {
        ThrowIfDisposed();

        long address = 0;
        var regions = new List<MemoryBasicInformation64>();

        while (true)
        {
            var dwResult = VirtualQueryEx(Handle, (nint)address, out var info, (uint)Marshal.SizeOf<MemoryBasicInformation64>());

            if (dwResult == 0)
            {
                break;
            }

            if ((info.State & 0x1000) != 0 && (info.Protect & 0x100) == 0)
            {
                regions.Add(info);
            }

            address = info.BaseAddress + info.RegionSize;
        }

        return regions;
    }

    public byte[] ReadMemory(long address, long size, out nint lpNumberOfBytesRead)
    {
        ThrowIfDisposed();
        
        var result = new byte[size];
        
        ReadProcessMemory(Handle, (nint)address, result, (nint)size, out lpNumberOfBytesRead);
        
        return result;
    }

    private bool _disposed;

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException("Scanner is disposed");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _ = CloseHandle(Handle);
    }
}

public sealed class Hublou
{
    private readonly Vector2ds _mapSize;
    private Grid<TileType>? _result;

    public bool Failed { get; private set; }

    public bool TryGetResult([NotNullWhen(true)] out Grid<TileType>? grid)
    {
        if (Failed)
        {
            grid = null;
            return false;
        }

        grid = _result;

        return grid != null;
    }

    public Hublou(Vector2ds mapSize)
    {
        _mapSize = mapSize;

        try
        {
            var thread = new Thread(RunScans);
            thread.Start();
        }
        catch (Exception e)
        {
            Util.LogError($"Failed to start hublou: {e}");
        }
    }

    private void RunScans()
    {
        var retries = 0;

        while (retries++ < 5)
        {
            try
            {
                _result = Scan(_mapSize);

                Util.LogInfo("Scan success!");

                if (_result != null)
                {
                    return;
                }
            }
            catch (Exception e)
            {
                Util.LogError($"Scan failure: {e}");
                Thread.Sleep(100);
            }
        }

        Failed = true;
    }

    private static Grid<TileType>? Scan(Vector2ds mapSize)
    {
        var windowTitles = new HashSet<string>
        {
            "geam", "Gameplay", "Game Controller", "Send Manual Command"
        };

        var processes = Process
            .GetProcesses()
            .Where(process => windowTitles.Any(t => process.MainWindowTitle.Contains(t)))
            .ToArray();

        // Identifies the bedrock edge:
        var query = new byte[mapSize.X];
      
        for (var i = 0; i < mapSize.X; i++)
        {
            query[i] = 66;
        }

        foreach (var process in processes)
        {
            using var scanner = new MemoryScanner(process);

            foreach (var info in scanner.MapMemoryRegions())
            {
                var memory = scanner.ReadMemory(info.BaseAddress, info.RegionSize, out var read);

                var index = IndexOf(memory, query);

                if (index == -1)
                {
                    continue;
                }

                var span = memory.AsSpan(index, mapSize.X * mapSize.Y);
                var grid = new Grid<TileType>(mapSize);
              
                for (var y = 0; y < mapSize.Y; y++)
                {
                    for (var x = 0; x < mapSize.X; x++)
                    {
                        var c = (char)span[y * mapSize.X + x];

                        grid[x, y] = c switch
                        {
                            'X' => TileType.Stone,
                            'A' => TileType.Cobble,
                            'B' => TileType.Bedrock,
                            'C' => TileType.Iron,
                            'D' => TileType.Osmium,
                            'E' => TileType.Base,
                            'F' => TileType.Acid,
                            '.' => TileType.Dirt,
                            _ => TileType.Unknown
                        };
                    }
                }

                return grid;
            }
        }

        return null;
    }

    // Brute force scan:

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (Matches(haystack, needle, i))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool Matches(byte[] haystack, byte[] needle, int start)
    {
        if (needle.Length + start > haystack.Length)
        {
            return false;
        }

        for (var i = 0; i < needle.Length; i++)
        {
            if (needle[i] != haystack[i + start])
            {
                return false;
            }
        }

        return true;
    }
}