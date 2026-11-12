using System;
using System.IO;
using System.Text.Json;

namespace MikroLab.Presentation
{
    /// <summary>
    /// User settings of the studio. The options follow the ones of the
    /// laboratory bench emulator (УМПК-80): the address the assembled program
    /// is written to, automatic assembly and disassembly when a file is
    /// opened, the tumblers of the bench and the execution speed.
    /// </summary>
    public sealed class MikroLabSettings
    {
        public int SpeedIndex { get; set; }

        public bool PauseOnHalt { get; set; }

        public bool StepToNextBranch { get; set; }

        public bool TraceEveryInstruction { get; set; }

        public bool AutoAssembleOnOpen { get; set; } = true;

        public bool AutoDisassembleOnOpen { get; set; } = true;

        public string LoadAddress { get; set; } = "8000h";

        public bool Tumbler1 { get; set; }

        public bool Tumbler2 { get; set; }

        public bool Tumbler3 { get; set; }

        public int EditorFontScale { get; set; } = 100;

        public bool ShowExecutionLog { get; set; } = true;

        public bool ShowDevices { get; set; } = true;

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        /// <summary>Path of the settings file: next to the executable if possible.</summary>
        public static string SettingsPath
        {
            get
            {
                string local = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MikroLab", "settings.json");
                return local;
            }
        }

        public static MikroLabSettings Load()
        {
            try
            {
                string path = SettingsPath;
                if (!File.Exists(path))
                    return new MikroLabSettings();

                string json = File.ReadAllText(path);
                MikroLabSettings settings = JsonSerializer.Deserialize<MikroLabSettings>(json, Options);
                return settings ?? new MikroLabSettings();
            }
            catch
            {
                return new MikroLabSettings();
            }
        }

        public void Save()
        {
            try
            {
                string path = SettingsPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
            }
            catch
            {
                // настройки не критичны: молча продолжаем работу
            }
        }
    }
}
