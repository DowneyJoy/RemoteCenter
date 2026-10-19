using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace RemoteCenter
{
    /// Windows 原生“打开文件”对话框（comdlg32），支持多选，供服务端选择视频/图片文件。
    public static class Win32FilePicker
    {
        const int OFN_ALLOWMULTISELECT = 0x00000200;
        const int OFN_HIDEREADONLY = 0x00000004;
        const int OFN_NOCHANGEDIR = 0x00000008;
        const int OFN_PATHMUSTEXIST = 0x00000800;
        const int OFN_FILEMUSTEXIST = 0x00001000;
        const int OFN_EXPLORER = 0x00080000;
        const int BufferChars = 32768;

        [StructLayout(LayoutKind.Sequential)]
        struct OpenFileNameW
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public IntPtr lpstrFilter;
            public IntPtr lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public IntPtr lpstrFile;
            public int nMaxFile;
            public IntPtr lpstrFileTitle;
            public int nMaxFileTitle;
            public IntPtr lpstrInitialDir;
            public IntPtr lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public IntPtr lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public IntPtr lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int FlagsEx;
        }

        [DllImport("user32.dll")]
        static extern IntPtr GetActiveWindow();

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetOpenFileNameW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetOpenFileNameW(ref OpenFileNameW ofn);

        /// 返回用户选中的文件绝对路径数组（支持多选）；取消或失败返回 null。仅支持 Windows。
        public static string[] PickMediaFiles()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                UnityEngine.Debug.LogWarning("[Win32FilePicker] 当前平台不支持系统文件对话框");
                return null;
            }

            // 双 \0 结尾的过滤串；过滤模式支持分号分隔的多扩展名
            const string filter =
                "媒体文件 (*.mp4;*.mov;*.avi;*.wmv;*.mkv;*.webm;*.jpg;*.jpeg;*.png)\0" +
                "*.mp4;*.mov;*.avi;*.wmv;*.mkv;*.webm;*.jpg;*.jpeg;*.png\0" +
                "视频文件 (*.mp4;*.mov;*.avi;*.wmv;*.mkv;*.webm)\0*.mp4;*.mov;*.avi;*.wmv;*.mkv;*.webm\0" +
                "图片文件 (*.jpg;*.jpeg;*.png)\0*.jpg;*.jpeg;*.png\0" +
                "所有文件 (*.*)\0*.*\0";
            IntPtr filterPtr = Marshal.StringToHGlobalUni(filter);
            IntPtr buffer = Marshal.AllocHGlobal(2 * BufferChars);
            IntPtr titlePtr = Marshal.StringToHGlobalUni("选择要发送的视频/图片（可多选）");
            IntPtr defExtPtr = Marshal.StringToHGlobalUni("mp4");
            try
            {
                Marshal.WriteInt16(buffer, 0); // 缓冲区需以空串初始化
                var ofn = new OpenFileNameW();
                ofn.lStructSize = Marshal.SizeOf(typeof(OpenFileNameW));
                ofn.hwndOwner = GetActiveWindow();
                ofn.lpstrFilter = filterPtr;
                ofn.nFilterIndex = 1;
                ofn.lpstrFile = buffer;
                ofn.nMaxFile = BufferChars;
                ofn.lpstrTitle = titlePtr;
                ofn.lpstrDefExt = defExtPtr;
                ofn.Flags = OFN_EXPLORER | OFN_ALLOWMULTISELECT | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_HIDEREADONLY | OFN_NOCHANGEDIR;

                if (!GetOpenFileNameW(ref ofn)) return null; // 用户取消或打开失败

                // 多选时缓冲区为 “目录\0文件1\0文件2\0\0”；单选为完整路径
                var parts = new List<string>();
                int charOffset = 0;
                while (charOffset < BufferChars)
                {
                    string part = Marshal.PtrToStringUni((IntPtr)(buffer.ToInt64() + charOffset * 2));
                    if (string.IsNullOrEmpty(part)) break;
                    parts.Add(part);
                    charOffset += part.Length + 1;
                }
                if (parts.Count == 0) return null;
                if (parts.Count == 1) return new[] { parts[0] };

                string dir = parts[0];
                var result = new List<string>(parts.Count - 1);
                for (int i = 1; i < parts.Count; i++)
                    result.Add(Path.Combine(dir, parts[i]));
                return result.ToArray();
            }
            finally
            {
                Marshal.FreeHGlobal(filterPtr);
                Marshal.FreeHGlobal(buffer);
                Marshal.FreeHGlobal(titlePtr);
                Marshal.FreeHGlobal(defExtPtr);
            }
        }
    }
}
