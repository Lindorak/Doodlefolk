using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Doodlefolk;

/// <summary>Talking to the copy that's already running. Starting Doodlefolk again opens its Studio (instead of doing
/// nothing); "Doodlefolk.exe --quit" closes it; "--studio" opens the Studio. These are what the taskbar button's
/// right-click menu (a jump list: "Open Studio", "Quit Doodlefolk") runs.</summary>
sealed partial class App
{
    const string QuitSignal = "Doodlefolk.Signal.Quit", StudioSignal = "Doodlefolk.Signal.Studio";
    EventWaitHandle? _quitSig, _studioSig;

    /// <summary>In the second copy: pass the message on to the running one.</summary>
    public static void SignalRunning(string[] args)
    {
        string name = args.Contains("--quit") ? QuitSignal : StudioSignal;
        try { using var e = EventWaitHandle.OpenExisting(name); e.Set(); }
        catch (WaitHandleCannotBeOpenedException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>In the running copy: listen for those messages (on the thread pool, acted on in the UI thread).</summary>
    void ListenForSignals()
    {
        if (_selfTest || _trailer) return;
        _quitSig = new EventWaitHandle(false, EventResetMode.AutoReset, QuitSignal);
        _studioSig = new EventWaitHandle(false, EventResetMode.AutoReset, StudioSignal);
        var ui = SynchronizationContext.Current;
        ThreadPool.RegisterWaitForSingleObject(_quitSig, (_, _) => ui?.Post(_ => { World.Log("quit asked for from the taskbar"); ExitThread(); }, null), null, -1, false);
        ThreadPool.RegisterWaitForSingleObject(_studioSig, (_, _) => ui?.Post(_ => OpenStudio(), null), null, -1, false);
        try { JumpList.Set(); } catch (Exception e) { World.Log("jump list: " + e.Message); }
    }
}

/// <summary>The taskbar button's right-click menu: "Open Studio" and "Quit Doodlefolk" above Windows' own entries
/// (ICustomDestinationList, the shell's jump list; no WPF needed).</summary>
static class JumpList
{
    public static void Set()
    {
        string exe = Environment.ProcessPath ?? Application.ExecutablePath;
        var list = (ICustomDestinationList)new DestinationList();
        var riid = typeof(IObjectArray).GUID;
        list.BeginList(out _, ref riid, out _);
        var tasks = (IObjectCollection)new EnumerableObjectCollection();
        tasks.AddObject(Link(exe, "--studio", "Open Studio", "Draw people, change looks and settings"));
        tasks.AddObject(Link(exe, "--quit", "Quit Doodlefolk", "Close Doodlefolk (everyone's saved)"));
        list.AddUserTasks((IObjectArray)tasks);
        list.CommitList();
    }

    static IShellLinkW Link(string exe, string args, string title, string tip)
    {
        var link = (IShellLinkW)new ShellLink();
        link.SetPath(exe);
        link.SetArguments(args);
        link.SetDescription(tip);
        link.SetIconLocation(exe, 0);
        var store = (IPropertyStore)link;
        var key = new PropertyKey(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);   // System.Title
        var v = new PropVariant(title);
        store.SetValue(ref key, ref v);
        store.Commit();
        v.Clear();
        return link;
    }

    [ComImport, Guid("77F10CF0-3DB5-4966-B520-B7C54FD35ED6"), ClassInterface(ClassInterfaceType.None)] class DestinationList { }
    [ComImport, Guid("2D3468C1-36A7-43B6-AC24-D3F02FD9607A"), ClassInterface(ClassInterfaceType.None)] class EnumerableObjectCollection { }
    [ComImport, Guid("00021401-0000-0000-C000-000000000046"), ClassInterface(ClassInterfaceType.None)] class ShellLink { }

    [ComImport, Guid("6332DEBF-87B5-4670-90C0-5E57B408A49E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICustomDestinationList
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string id);
        void BeginList(out uint minSlots, [In] ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object removed);
        void AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string category, [MarshalAs(UnmanagedType.Interface)] IObjectArray items);
        void AppendKnownCategory(int category);
        void AddUserTasks([MarshalAs(UnmanagedType.Interface)] IObjectArray tasks);
        void CommitList();
        void GetRemovedDestinations([In] ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object removed);
        void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string? id);
        void AbortList();
    }

    [ComImport, Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IObjectArray
    {
        void GetCount(out uint count);
        void GetAt(uint i, [In] ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object o);
    }

    [ComImport, Guid("5632B1A4-E38A-400A-928A-D4CD63230295"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IObjectCollection
    {
        void GetCount(out uint count);
        void GetAt(uint i, [In] ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object o);
        void AddObject([MarshalAs(UnmanagedType.Interface)] object o);
        void AddFromArray([MarshalAs(UnmanagedType.Interface)] IObjectArray source);
        void RemoveObjectAt(uint i);
        void Clear();
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file, int max, IntPtr fd, uint flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder name, int max);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder dir, int max);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder args, int max);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short key);
        void SetHotkey(short key);
        void GetShowCmd(out int cmd);
        void SetShowCmd(int cmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder path, int max, out int icon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int icon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string rel, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint i, out PropertyKey key);
        void GetValue([In] ref PropertyKey key, out PropVariant v);
        void SetValue([In] ref PropertyKey key, [In] ref PropVariant v);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct PropertyKey
    {
        public Guid Fmtid; public uint Pid;
        public PropertyKey(Guid f, uint p) { Fmtid = f; Pid = p; }
    }

    [StructLayout(LayoutKind.Explicit)]
    struct PropVariant
    {
        [FieldOffset(0)] ushort _vt;
        [FieldOffset(8)] IntPtr _ptr;
        public PropVariant(string s) { _vt = 31; _ptr = Marshal.StringToCoTaskMemUni(s); }   // VT_LPWSTR
        public void Clear() { if (_ptr != IntPtr.Zero) { Marshal.FreeCoTaskMem(_ptr); _ptr = IntPtr.Zero; } _vt = 0; }
    }
}
