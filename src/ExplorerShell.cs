using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace QuickCut
{
    // Store version only: starts programs and websites through the Explorer that is already running,
    // instead of starting them ourselves.
    //
    // Anything a packaged app starts runs inside its package too, so the programs it opens would write
    // their new AppData files and HKCU values to QuickCut's private copy instead of the real ones (an
    // app opened from the ring would lose its settings when opened any other way). The Store doesn't
    // allow turning that off (unvirtualizedResources), and the breakaway setting of CreateProcess is
    // ignored for a packaged app. Asking Explorer's desktop window to ShellExecute works: the program
    // starts from explorer.exe, as it would from the Start menu, with arguments and folder intact.
    // (Raymond Chen's "run a program unelevated from an elevated one" technique.)
    static class ExplorerShell
    {
        // Throws when there is no Explorer to ask (it crashed, or another shell is in use).
        public static void Run(string file, string args, string dir)
        {
            object windows = null, desktop = null, browser = null, view = null, folderView = null, app = null;
            try
            {
                windows = Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_ShellWindows, true));
                object loc = 0 /* CSIDL_DESKTOP */, root = Type.Missing;
                int hwnd;
                desktop = ((IShellWindows)windows).FindWindowSW(ref loc, ref root, SWC_DESKTOP, out hwnd, SWFO_NEEDDISPATCH);
                if (desktop == null) throw new InvalidOperationException("Explorer isn't running.");

                Guid sid = SID_STopLevelBrowser, iid = typeof(IShellBrowser).GUID;
                browser = ((IServiceProvider)desktop).QueryService(ref sid, ref iid);
                view = ((IShellBrowser)browser).QueryActiveShellView();
                Guid idisp = IID_IDispatch;
                folderView = ((IShellView)view).GetItemObject(SVGIO_BACKGROUND, ref idisp);

                app = folderView.GetType().InvokeMember("Application", BindingFlags.GetProperty, null, folderView, null);
                app.GetType().InvokeMember("ShellExecute", BindingFlags.InvokeMethod, null, app,
                    new object[] { file, args ?? "", dir ?? "", "", 1 /* SW_SHOWNORMAL */ });
            }
            finally
            {
                foreach (object o in new[] { app, folderView, view, browser, desktop, windows })
                    if (o != null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o);
            }
        }

        static readonly Guid CLSID_ShellWindows = new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
        static readonly Guid SID_STopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
        static readonly Guid IID_IDispatch = new Guid("00020400-0000-0000-C000-000000000046");
        const int SWC_DESKTOP = 8, SWFO_NEEDDISPATCH = 1;
        const uint SVGIO_BACKGROUND = 0;

        // Only the methods used are spelled out; the others hold their place in the vtable.
        [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        interface IShellWindows
        {
            void get_Count(); void Item(); void _NewEnum(); void Register(); void RegisterPending();
            void Revoke(); void OnNavigate(); void OnActivated();
            [return: MarshalAs(UnmanagedType.IDispatch)]
            object FindWindowSW(ref object loc, ref object locRoot, int swClass, out int hwnd, int options);
        }

        [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IServiceProvider
        {
            [return: MarshalAs(UnmanagedType.IUnknown)]
            object QueryService(ref Guid sid, ref Guid iid);
        }

        [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellBrowser
        {
            void GetWindow(); void ContextSensitiveHelp(); void InsertMenusSB(); void SetMenuSB(); void RemoveMenusSB();
            void SetStatusTextSB(); void EnableModelessSB(); void TranslateAcceleratorSB(); void BrowseObject();
            void GetViewStateStream(); void GetControlWindow(); void SendControlMsg();
            IShellView QueryActiveShellView();
        }

        [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellView
        {
            void GetWindow(); void ContextSensitiveHelp(); void TranslateAccelerator(); void EnableModeless();
            void UIActivate(); void Refresh(); void CreateViewWindow(); void DestroyViewWindow(); void GetCurrentInfo();
            void AddPropertySheetPages(); void SaveViewState(); void SelectItem();
            [return: MarshalAs(UnmanagedType.IUnknown)]
            object GetItemObject(uint item, ref Guid iid);
        }
    }
}
