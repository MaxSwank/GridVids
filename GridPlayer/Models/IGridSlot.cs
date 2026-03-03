using System;
using System.Diagnostics;

namespace GridVids.Models
{
    public interface IGridSlot
    {
        IntPtr WindowHandle { get; }
        Process? CurrentProcess { get; }
        string CurrentVideoPath { get; }
        Process? UpdateProcess(Process? newProcess, string newVideoPath);
    }
}
