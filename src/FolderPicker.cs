using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Paranoidal
{
    /// <summary>
    /// Choosing a folder with the Explorer-style window of Windows (IFileOpenDialog with FOS_PICKFOLDERS: address bar,
    /// navigation pane, search, «Выбор папки»), not the old «Обзор папок» tree of FolderBrowserDialog, which .NET
    /// Framework 4.8 always shows (owner, 2026-11-10). Falls back to FolderBrowserDialog if the shell object cannot be
    /// created. The window is drawn by Windows: its colours follow the Windows theme.
    /// </summary>
    static class FolderPicker
    {
        /// <summary>The chosen folder; null when the player cancelled.</summary>
        public static string Pick(IWin32Window owner, string title, string initial)
        {
            IFileOpenDialog dlg = null;
            try { dlg = (IFileOpenDialog)new FileOpenDialogCom(); }
            catch { return Fallback(owner, title, initial); }
            try
            {
                uint opts;
                dlg.GetOptions(out opts);
                dlg.SetOptions(opts | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST | FOS_NOCHANGEDIR);
                if (!string.IsNullOrEmpty(title)) dlg.SetTitle(title);
                var start = Existing(initial);
                if (start != null)
                {
                    IShellItem item;
                    var iid = typeof(IShellItem).GUID;
                    if (SHCreateItemFromParsingName(start, IntPtr.Zero, ref iid, out item) == 0 && item != null) dlg.SetFolder(item);
                }
                var hr = dlg.Show(owner != null ? owner.Handle : IntPtr.Zero);
                if (hr == ErrorCancelled) return null;
                if (hr != 0) Marshal.ThrowExceptionForHR(hr);
                IShellItem result;
                dlg.GetResult(out result);
                string path;
                result.GetDisplayName(SIGDN_FILESYSPATH, out path);
                return string.IsNullOrEmpty(path) ? null : path.TrimEnd('\\');
            }
            finally { Marshal.FinalReleaseComObject(dlg); }
        }

        /// <summary>The folder itself or its nearest existing parent (a moved game, a deleted mods folder).</summary>
        static string Existing(string path)
        {
            try
            {
                for (var d = string.IsNullOrEmpty(path) ? null : new DirectoryInfo(path); d != null; d = d.Parent)
                    if (d.Exists) return d.FullName;
            }
            catch { }
            return null;
        }

        static string Fallback(IWin32Window owner, string title, string initial)
        {
            using (var f = new FolderBrowserDialog { Description = title, ShowNewFolderButton = true, SelectedPath = Existing(initial) ?? "" })
                return f.ShowDialog(owner) == DialogResult.OK ? f.SelectedPath : null;
        }

        const uint FOS_NOCHANGEDIR = 0x8, FOS_PICKFOLDERS = 0x20, FOS_FORCEFILESYSTEM = 0x40, FOS_PATHMUSTEXIST = 0x800;
        const uint SIGDN_FILESYSPATH = 0x80058000;
        const int ErrorCancelled = unchecked((int)0x800704C7);   // HRESULT_FROM_WIN32(ERROR_CANCELLED)

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IShellItem item);

        [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
        class FileOpenDialogCom { }

        /// <summary>IFileOpenDialog: the methods of IModalWindow and IFileDialog in vtable order (shobjidl_core.h).</summary>
        [ComImport, Guid("D57C7288-D4AD-4768-BE02-9D969532D960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint count, IntPtr specs);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem item);
            void SetFolder(IShellItem item);
            void GetFolder(out IShellItem item);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
            void AddPlace(IShellItem item, int place);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string ext);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
            void GetResults(out IntPtr items);
            void GetSelectedItems(out IntPtr items);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem other, uint hint, out int order);
        }
    }
}
