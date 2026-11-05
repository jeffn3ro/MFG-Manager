using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using WixToolset.Dtf.WindowsInstaller;

namespace MfgManager.Installer.CustomActions;

/// <summary>
/// The "Change..." button on the install folder page. Windows Installer's own Browse dialog is the old folder tree;
/// this opens the folder picker Explorer uses instead. It runs as you, only while the setup window is open.
/// </summary>
public static class FolderPicker
{
    private const string ProductFolderName = "MFG Manager";

    [CustomAction]
    public static ActionResult PickInstallFolder(Session session)
    {
        string current = session["INSTALLFOLDER"];
        // So the picker sits on top of the setup window.
        IntPtr owner = FindWindow(null, session["ProductName"] + " Setup");
        string? picked = null;

        // The picker is a COM window, and those need an STA thread.
        var thread = new Thread(() => picked = Show(current, owner));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (picked is not null) session["INSTALLFOLDER"] = InstallFolderFor(picked);
        return ActionResult.Success;
    }

    /// <summary>"D:\Apps" becomes "D:\Apps\MFG Manager\", so the app gets a folder of its own, like other installers do.</summary>
    public static string InstallFolderFor(string picked)
    {
        string folder = Path.GetFullPath(picked);
        if (!new DirectoryInfo(folder).Name.Equals(ProductFolderName, StringComparison.OrdinalIgnoreCase))
            folder = Path.Combine(folder, ProductFolderName);
        return folder.TrimEnd('\\') + """\""";
    }

    /// <summary>Shows the picker. Returns the chosen folder, or null if you cancelled.</summary>
    private static string? Show(string currentFolder, IntPtr owner)
    {
        var dialog = (IFileOpenDialog)new FileOpenDialog();
        try
        {
            dialog.SetOptions(FosPickFolders | FosForceFileSystem | FosPathMustExist);
            dialog.SetTitle("Choose where to install MFG Manager");
            // Start in the folder above the current choice (C:\Program Files), so the choice is easy to see.
            if (ExistingParent(currentFolder) is { } start && SHCreateItemFromParsingName(start, IntPtr.Zero, typeof(IShellItem).GUID, out IShellItem? item) == 0)
                dialog.SetFolder(item);

            // Cancelled.
            if (dialog.Show(owner) != 0) return null;
            dialog.GetResult(out IShellItem? result);
            result.GetDisplayName(SigdnFileSysPath, out string? path);
            return path;
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }

    private static string? ExistingParent(string? folder)
    {
        for (DirectoryInfo? dir = string.IsNullOrEmpty(folder) ? null : Directory.GetParent(folder!.TrimEnd('\\')); dir is not null; dir = dir.Parent)
            if (dir.Exists) return dir.FullName;
        return null;
    }

    // ---------------- Windows' folder picker (IFileOpenDialog) ----------------

    private const uint FosPickFolders = 0x20, FosForceFileSystem = 0x40, FosPathMustExist = 0x800;
    private const uint SigdnFileSysPath = 0x80058000;

    // Loaded from Windows' own folder only, so a file with the same name somewhere else can't be picked up instead.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr FindWindow(string? className, string windowName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItem item);

    [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    private class FileOpenDialog { }

    // Only the methods up to GetResult are declared; their order must match Windows' own list.
    [ComImport, Guid("D57C7288-D4AD-4768-BE02-9D969532D960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig] int Show(IntPtr owner);
        void SetFileTypes(uint count, IntPtr filters);
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
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid riid, out IntPtr result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint form, [MarshalAs(UnmanagedType.LPWStr)] out string name);
    }
}
