using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MikroLab.Core.Machine;

namespace MikroLab.Core.Formats
{
    /// <summary>
    /// Source-generated metadata keeps machine-state JSON working in trimmed
    /// self-contained builds, where reflection-based serialization is disabled.
    /// </summary>
    [JsonSourceGenerationOptions(
        WriteIndented = true,
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        UseStringEnumConverter = true)]
    [JsonSerializable(typeof(MachineStateFile))]
    internal sealed partial class MachineStateJsonContext : JsonSerializerContext
    {
    }

    /// <summary>Root object of a saved MikroLab machine-state file.</summary>
    public sealed class MachineStateFile
    {
        public string Format { get; set; } = MachineStateSerializer.FormatName;
        public int Version { get; set; } = MachineStateSerializer.CurrentVersion;
        public DateTime SavedAt { get; set; } = DateTime.UtcNow;
        public bool ProgramLoaded { get; set; }
        public ushort EntryPoint { get; set; } = 0x8000;
        public MachineSnapshot Snapshot { get; set; }
    }

    /// <summary>
    /// JSON persistence for a complete machine snapshot: CPU registers, flags,
    /// devices, 64 KiB memory, debug call stack and HLT state. Memory is stored
    /// as base64 by System.Text.Json, so the file stays compact and text-only.
    /// </summary>
    public static class MachineStateSerializer
    {
        public const string FormatName = "MikroLab.MachineState";
        public const int CurrentVersion = 1;

        public static string Serialize(MachineSnapshot snapshot, bool programLoaded = false,
            ushort entryPoint = 0x8000)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.Memory == null || snapshot.Memory.Length != 0x10000)
                throw new ArgumentException("Snapshot must contain a 64 KiB memory dump.", nameof(snapshot));

            var file = new MachineStateFile
            {
                SavedAt = DateTime.UtcNow,
                ProgramLoaded = programLoaded,
                EntryPoint = entryPoint,
                Snapshot = snapshot
            };
            return JsonSerializer.Serialize(file, MachineStateJsonContext.Default.MachineStateFile);
        }

        public static MachineStateFile Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidDataException("Machine state is empty.");

            MachineStateFile file;
            try
            {
                file = JsonSerializer.Deserialize(json, MachineStateJsonContext.Default.MachineStateFile);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("Machine state is not valid JSON: " + ex.Message, ex);
            }

            Validate(file);
            return file;
        }

        public static void Save(string path, MachineSnapshot snapshot, bool programLoaded = false,
            ushort entryPoint = 0x8000)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            File.WriteAllText(path, Serialize(snapshot, programLoaded, entryPoint), new UTF8Encoding(false));
        }

        public static MachineStateFile Load(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            return Deserialize(File.ReadAllText(path));
        }

        private static void Validate(MachineStateFile file)
        {
            if (file == null)
                throw new InvalidDataException("Machine state has no root object.");
            if (!string.Equals(file.Format, FormatName, StringComparison.Ordinal))
                throw new InvalidDataException("Unsupported machine state format '" + file.Format + "'.");
            if (file.Version < 1 || file.Version > CurrentVersion)
                throw new InvalidDataException("Unsupported machine state version " + file.Version + ".");
            if (file.Snapshot == null)
                throw new InvalidDataException("Machine state contains no snapshot.");
            if (file.Snapshot.Memory == null || file.Snapshot.Memory.Length != 0x10000)
                throw new InvalidDataException("Machine state memory dump must be exactly 64 KiB.");
            if (file.Snapshot.CallStackDepth < 0)
                throw new InvalidDataException("Machine state has a negative call stack depth.");
            if (file.Snapshot.CallStack != null && file.Snapshot.CallStack.Length > 0x10000)
                throw new InvalidDataException("Machine state call stack is too deep.");
        }
    }
}
