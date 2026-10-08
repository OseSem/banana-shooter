using System.ComponentModel;
using System.Diagnostics;
using System;
using System.Runtime.InteropServices;
using Debug = UnityEngine.Debug;

namespace CodingDaniel.FileBrowser
{
    public class LibWrap
    {
        //BOOL GetOpenFileName(LPOPENFILENAME lpofn);

        [ DllImport( "Comdlg32.dll", CharSet=CharSet.Auto )]                
        public static extern bool GetOpenFileName([ In, Out ] OpenFileName ofn );   
    }

    [StructLayout(LayoutKind.Sequential, CharSet =CharSet.Auto)]
    public class OpenFileName
    {
        public int      structSize = 0;
        public IntPtr   dlgOwner = IntPtr.Zero; 
        public IntPtr   instance = IntPtr.Zero;

        public String   filter = null;
        public String   customFilter = null;
        public int      maxCustFilter = 0;
        public int      filterIndex = 0;

        public String   file = null;
        public int      maxFile = 0;

        public String   fileTitle = null;
        public int      maxFileTitle = 0;

        public String   initialDir = null;

        public String   title = null;   

        public int      flags = 0; 
        public short    fileOffset = 0;
        public short    fileExtension = 0;

        public String   defExt = null; 

        public IntPtr   custData = IntPtr.Zero;  
        public IntPtr   hook = IntPtr.Zero;  

        public String   templateName = null; 

        public IntPtr   reservedPtr = IntPtr.Zero; 
        public int      reservedInt = 0;
        public int      flagsEx = 0;
    }

    public enum FileType
    {
        None,
        Text,
        Texture,
        Video,
        Audio,
    }

    public static class FileIOUtil
    {
        public static string OpenFileDialog(FileType type)
        {
#if UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
            return OpenLinuxFileDialog(type);
#else
            OpenFileName ofn = new OpenFileName();
            ofn.structSize = Marshal.SizeOf(ofn);
            switch (type)
            {
                case FileType.None:
                    ofn.filter = "All Files\0*.*\0";
                    break;
                case FileType.Texture:
                    ofn.filter = "Texture2D Files\0*.png\0*.jpg\0";
                    break;
                case FileType.Audio:
                    ofn.filter = "Audio Files\0*.mp3\0*.wav\0";
                    break;
            }
            
            ofn.file = new String( new char[ 256 ]);
            ofn.maxFile = ofn.file.Length;

            ofn.fileTitle = new String( new char[ 64 ]);
            ofn.maxFileTitle = ofn.fileTitle.Length;    

            ofn.title = "Select Files";
            // ofn.defExt = "png;jpg";
            
            ofn.flags = 0x00000008;
            if (LibWrap.GetOpenFileName(ofn))
            {
                // Debug.Log(ofn.file);
                return ofn.file;
            }

            else
            {
                Debug.Log("failed");
            }
            return null;
#endif
        }

        private static string OpenLinuxFileDialog(FileType type)
        {
            string patterns = type switch
            {
                FileType.Texture => "*.png *.jpg",
                FileType.Audio => "*.mp3 *.wav",
                _ => null,
            };

            string zenityArguments = "--file-selection --title=\"Select Files\"" + (patterns == null ? "" : $" --file-filter=\"{patterns}\"");
            string path = RunDialog("zenity", zenityArguments, out bool started);
            if (started) return path;

            string kdialogArguments = "--getopenfilename ." + (patterns == null ? "" : $" \"{patterns}\"");
            path = RunDialog("kdialog", kdialogArguments, out started);
            if (!started) Debug.LogError("No file dialog available. Install zenity or kdialog.");
            return path;
        }
        
        private static string RunDialog(string program, string arguments, out bool started)
        {
            started = false;
            try
            {
                using Process process = Process.Start(new ProcessStartInfo(program, arguments)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                });
                if (process == null) return null;

                started = true;
                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                return process.ExitCode == 0 && output.Length > 0 ? output : null;
            }
            catch (Win32Exception)
            {
                return null;
            }
        }
    }
}
