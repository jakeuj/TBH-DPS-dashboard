using System;
using System.Runtime.InteropServices;

namespace TbhDpsMeter
{
    /// <summary>Thin wrapper over the Win32 common "Open File" dialog (comdlg32) so the user can pick a
    /// custom box-pickup .wav from the F5 panel instead of hand-editing the config. The dialog is modal,
    /// so callers should run <see cref="PickWav"/> on a background thread.</summary>
    internal static class FileDialog
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenFileName
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public string lpstrFilter;
            public string lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public string lpstrFile;
            public int nMaxFile;
            public string lpstrFileTitle;
            public int nMaxFileTitle;
            public string lpstrInitialDir;
            public string lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public string lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public string lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int FlagsEx;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetOpenFileNameW(ref OpenFileName ofn);

        private const int OFN_FILEMUSTEXIST = 0x00001000;
        private const int OFN_PATHMUSTEXIST = 0x00000800;
        private const int OFN_EXPLORER = 0x00080000;
        private const int OFN_NOCHANGEDIR = 0x00000008;

        /// <summary>Show the open dialog filtered to .wav; returns the chosen path, or null if cancelled/failed.</summary>
        public static string PickWav()
        {
            try
            {
                var ofn = new OpenFileName();
                ofn.lStructSize = Marshal.SizeOf(typeof(OpenFileName));
                ofn.lpstrFilter = "WAV audio (*.wav)\0*.wav\0All files (*.*)\0*.*\0\0";
                ofn.nFilterIndex = 1;
                ofn.lpstrFile = new string('\0', 260);
                ofn.nMaxFile = 260;
                ofn.lpstrFileTitle = new string('\0', 260);
                ofn.nMaxFileTitle = 260;
                ofn.lpstrTitle = "Select box pickup sound (.wav)";
                ofn.Flags = OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR;
                if (GetOpenFileNameW(ref ofn))
                {
                    string p = ofn.lpstrFile;
                    if (!string.IsNullOrEmpty(p)) return p.TrimEnd('\0');
                }
            }
            catch (Exception e) { Plugin.Logger?.LogWarning("[box] file dialog failed: " + e.Message); }
            return null;
        }
    }
}
