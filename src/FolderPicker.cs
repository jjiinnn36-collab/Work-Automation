using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PaymentAlert
{
    /// <summary>
    /// 윈도 기본 '폴더 선택' 창 (탐색기와 같은 모양, IFileOpenDialog + FOS_PICKFOLDERS).
    /// 웹 화면은 브라우저 보안상 폴더의 전체 경로를 알 수 없어, 이 PC 에서 도는 웹 서버가 대신 창을 띄운다.
    /// 요청 스레드와 따로 STA 스레드에서 띄우고, 브라우저 뒤에 숨지 않게 맨 위 창을 주인으로 삼는다.
    /// 고르면 경로, 취소하면 null.
    /// </summary>
    public static class FolderPicker
    {
        public static string Pick(string 제목, string 처음폴더)
        {
            string result = null;
            Exception error = null;
            var t = new Thread(delegate()
            {
                try { result = 창띄우기(제목, 처음폴더); }
                catch (Exception ex) { error = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
            t.Join();
            if (error != null) throw error;
            return result;
        }

        static string 창띄우기(string 제목, string 처음폴더)
        {
            using (var owner = new Form())
            {
                // 보이지 않는 맨 위 창 — 대화상자가 브라우저 뒤에 뜨지 않게 한다.
                owner.ShowInTaskbar = false;
                owner.FormBorderStyle = FormBorderStyle.None;
                owner.StartPosition = FormStartPosition.CenterScreen;
                owner.Size = new System.Drawing.Size(1, 1);
                owner.Opacity = 0;
                owner.TopMost = true;
                owner.Show();
                owner.Activate();

                var dlg = (IFileOpenDialog)new FileOpenDialogRcw();
                try
                {
                    uint opts;
                    dlg.GetOptions(out opts);
                    dlg.SetOptions(opts | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
                    if (!string.IsNullOrEmpty(제목)) dlg.SetTitle(제목);
                    if (!string.IsNullOrEmpty(처음폴더) && Directory.Exists(처음폴더))
                    {
                        IShellItem start;
                        Guid iid = typeof(IShellItem).GUID;
                        if (SHCreateItemFromParsingName(처음폴더, IntPtr.Zero, ref iid, out start) == 0 && start != null)
                            dlg.SetFolder(start);
                    }
                    int hr = dlg.Show(owner.Handle);
                    if (hr == ERROR_CANCELLED) return null;
                    if (hr != 0) Marshal.ThrowExceptionForHR(hr);
                    IShellItem item;
                    dlg.GetResult(out item);
                    string path;
                    item.GetDisplayName(SIGDN_FILESYSPATH, out path);
                    return path;
                }
                finally
                {
                    Marshal.ReleaseComObject(dlg);
                }
            }
        }

        const uint FOS_PICKFOLDERS = 0x20, FOS_FORCEFILESYSTEM = 0x40, FOS_PATHMUSTEXIST = 0x800;
        const uint SIGDN_FILESYSPATH = 0x80058000;
        const int ERROR_CANCELLED = unchecked((int)0x800704C7);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IShellItem item);

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        class FileOpenDialogRcw { }

        [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint c, IntPtr specs);
            void SetFileTypeIndex(uint i);
            void GetFileTypeIndex(out uint i);
            void Advise(IntPtr sink, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint fos);
            void GetOptions(out uint fos);
            void SetDefaultFolder(IShellItem si);
            void SetFolder(IShellItem si);
            void GetFolder(out IShellItem si);
            void GetCurrentSelection(out IShellItem si);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem si);
            void AddPlace(IShellItem si, int fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string ext);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
            void GetResults(out IntPtr enumerator);
            void GetSelectedItems(out IntPtr items);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem si);
            void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
            void GetAttributes(uint mask, out uint attrs);
            void Compare(IShellItem si, uint hint, out int order);
        }
    }
}
