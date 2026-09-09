// Steam Stats Editor original code, distributed under the repository's zlib license.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace SAM.Launcher
{
    internal static class Program
    {
        private static readonly string[] RequiredApplicationFiles =
        {
            "SAM.Picker.exe", "SAM.Picker.exe.config", "SAM.Game.exe", "SAM.Game.exe.config",
            "SAM.API.dll", "SAM.Batch.dll", "SAM.Submission.dll",
            "System.Resources.Extensions.dll", "System.Memory.dll", "System.Buffers.dll",
            "System.Runtime.CompilerServices.Unsafe.dll", "System.Numerics.Vectors.dll",
        };

        [STAThread]
        private static int Main(string[] args)
        {
            // Used by the Windows packaging check. This path never opens Steam or the picker.
            bool checkOnly = args.Length == 1 && args[0] == "--check-package";
            if (checkOnly) Console.OutputEncoding = new UTF8Encoding(false);
            try
            {
                if (args.Length != 0 && !checkOnly)
                {
                    throw new ArgumentException("不支持此启动参数。请直接双击 SteamStatsEditor.exe。");
                }

                string applicationDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app");
                var missing = RequiredApplicationFiles
                    .Where(name => !File.Exists(Path.Combine(applicationDirectory, name))).ToArray();
                if (missing.Length != 0)
                {
                    throw new FileNotFoundException(
                        "程序文件不完整。请先完整解压下载包，并让 app 文件夹与 SteamStatsEditor.exe 保持在同一目录。\n\n" +
                        "缺少文件：\n" + string.Join("\n", missing.Select(name => "app/" + name)));
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(applicationDirectory, "SAM.Picker.exe"),
                    WorkingDirectory = applicationDirectory,
                    UseShellExecute = false,
                };
                if (checkOnly)
                {
                    Console.WriteLine("Executable: " + startInfo.FileName);
                    Console.WriteLine("WorkingDirectory: " + startInfo.WorkingDirectory);
                    return 0;
                }

                // Keep the picker and game executable together, preserving their existing
                // relative paths and assembly probing. Never depend on the caller's cwd.
                using (Process process = Process.Start(startInfo))
                {
                    if (process == null) throw new IOException("未能启动游戏列表。");
                }
                return 0;
            }
            catch (Exception exception)
            {
                if (checkOnly)
                {
                    Console.Error.WriteLine(exception.Message);
                }
                else
                {
                    MessageBox.Show(exception.Message, "Steam Stats Editor — 启动失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return 2;
            }
        }
    }
}
