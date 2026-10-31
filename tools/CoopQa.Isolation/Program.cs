using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Test-copy provisioning only. This executable never launches a game, changes
// registry keys, connects to Steam, or accepts a path to an installed game.
internal static class Program
{
    static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2 || args[0] != "--slot-directory")
                throw new ArgumentException("Usage: CoopQa.Isolation --slot-directory <workspace>/local/slots/<0-9>");
            var slot = Path.GetFullPath(args[1]).TrimEnd(Path.DirectorySeparatorChar);
            var slotInfo = new DirectoryInfo(slot);
            if (slotInfo.Name.Length != 1 || !char.IsAsciiDigit(slotInfo.Name[0]) ||
                slotInfo.Parent?.Name != "slots" || slotInfo.Parent.Parent?.Name != "local")
                throw new InvalidOperationException("Only numbered physical copies under local/slots are allowed.");
            var workspace = slotInfo.Parent.Parent.Parent!.FullName;
            if (!File.Exists(Path.Combine(workspace, "Blasphemous.Multiplayer.sln")))
                throw new InvalidOperationException("Could not verify the project workspace.");
            var game = File.Exists(Path.Combine(slot, "Blasphemous.exe")) ? slot : Path.Combine(slot, "game");
            foreach (var path in new[] { slot, game }) RejectReparsePoints(path, workspace);
            using var identity = JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace, "local", "identity.json")));
            var expectedExe = identity.RootElement.GetProperty("ExeSha256").GetString();
            if (!Hash(Path.Combine(game, "Blasphemous.exe")).Equals(expectedExe, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The copied executable does not match the recorded game identity.");

            var product = "BlasCoopQA" + slotInfo.Name;
            var saveRoot = Path.Combine(slot, "qa-saves") + Path.DirectorySeparatorChar;
            var files = new[] {
                Path.Combine(game, "Blasphemous_Data", "data.unity3d"),
                Path.Combine(game, "Blasphemous_Data", "Managed", "Assembly-CSharp.dll"),
                Path.Combine(game, "Blasphemous_Data", "Managed", "Assembly-CSharp-firstpass.dll"),
                Path.Combine(game, "Blasphemous_Data", "app.info")
            };
            foreach (var file in files) { RejectReparsePoints(file, workspace); RequireSingleLink(file); }
            var manifestPath = Path.Combine(slot, "qa-isolation.json");
            if (File.Exists(manifestPath))
            {
                using var previous = JsonDocument.Parse(File.ReadAllText(manifestPath));
                foreach (var item in previous.RootElement.GetProperty("Files").EnumerateArray())
                    if (Hash(item.GetProperty("Path").GetString()!) != item.GetProperty("AfterSha256").GetString())
                        throw new InvalidOperationException("An already-isolated file changed; preserve the backups and investigate.");
                Console.WriteLine("Isolation already applied and hashes verified: " + product);
                return 0;
            }
            var backup = Path.Combine(slot, "qa-isolation-backups");
            if (Directory.Exists(backup))
                throw new InvalidOperationException("Incomplete previous isolation exists; restore its backups before retrying.");

            // Validate the exact target methods/serialized identity before writing anything.
            using (var bundle = new UnityBundle(files[0])) bundle.FindProduct("Blasphemous");
            using (var assembly = AssemblyDefinition.ReadAssembly(files[1])) ValidateGameMethods(assembly);
            using (var firstpass = AssemblyDefinition.ReadAssembly(files[2])) ValidatePlatformTypes(firstpass);
            Directory.CreateDirectory(backup);
            Directory.CreateDirectory(saveRoot);
            var records = new List<FileRecord>();
            foreach (var file in files)
            {
                var saved = Path.Combine(backup, Path.GetRelativePath(game, file));
                Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                File.Copy(file, saved, false);
                var before = Hash(file);
                if (Hash(saved) != before) throw new IOException("Backup verification failed: " + file);
                records.Add(new FileRecord { Path = file, Backup = saved, BeforeSha256 = before });
            }
            using (var bundle = new UnityBundle(files[0])) bundle.WriteProductCopy(files[0] + ".qa-tmp", product);
            File.Move(files[0] + ".qa-tmp", files[0], true);
            PatchGameAssembly(files[1], saveRoot);
            PatchPlatformAssembly(files[2]);
            File.WriteAllText(files[3], "TheGameKitchen\n" + product + "\n", new UTF8Encoding(false));
            using (var bundle = new UnityBundle(files[0])) bundle.FindProduct(product);
            foreach (var record in records) record.AfterSha256 = Hash(record.Path);
            var result = new {
                ProductName = product, SaveRoot = saveRoot, GameDirectory = game,
                SteamAchievements = "local helper; SteamUserStats mutators stubbed",
                CloudApis = "SteamRemoteStorage public methods return empty/default values",
                NativePlayerPrefs = "isolated by serialized PlayerSettings productName; verify Application.productName in game",
                Files = records
            };
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("Prepared isolated product " + product + "; saves: " + saveRoot);
            Console.WriteLine("Backups and before/after SHA-256 hashes: " + manifestPath);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("Isolation failed: " + error); return 1; }
    }

    sealed class FileRecord
    {
        public string Path { get; set; } = "";
        public string Backup { get; set; } = "";
        public string BeforeSha256 { get; set; } = "";
        public string AfterSha256 { get; set; } = "";
    }
    static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    static void RejectReparsePoints(string path, string workspace)
    {
        if (!Path.GetFullPath(path).StartsWith(workspace + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Path escapes the workspace.");
        for (FileSystemInfo? entry = File.Exists(path) ? new FileInfo(path) : new DirectoryInfo(path);
            entry != null && !entry.FullName.Equals(workspace, StringComparison.OrdinalIgnoreCase);
            entry = Directory.GetParent(entry.FullName))
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Reparse-point target refused: " + entry.FullName);
    }
    [StructLayout(LayoutKind.Sequential)]
    struct FileInformation
    {
        public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);
    static void RequireSingleLink(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Physical-copy verification requires Windows.");
        using var stream = File.OpenRead(path);
        if (!GetFileInformationByHandle(stream.SafeFileHandle, out var info) || info.Links != 1)
            throw new InvalidOperationException("Refusing a file with multiple links or an unverifiable link count: " + path);
    }

    static TypeDefinition Type(AssemblyDefinition assembly, string name) =>
        assembly.MainModule.Types.SingleOrDefault(t => t.FullName == name) ?? throw new InvalidDataException("Missing type: " + name);
    static MethodDefinition Method(TypeDefinition type, string name) =>
        type.Methods.SingleOrDefault(m => m.Name == name) ?? throw new InvalidDataException("Missing/ambiguous method: " + type.FullName + "." + name);
    static void ValidateGameMethods(AssemblyDefinition assembly)
    {
        var path = Method(Type(assembly, "Framework.Managers.PersistentManager"), "GetPathAppSettings");
        if (!path.IsStatic || path.Parameters.Count != 1 || path.ReturnType.FullName != "System.String") throw new InvalidDataException("Unexpected save-path signature.");
        _ = Method(Type(assembly, "Framework.Managers.AchievementsManager"), "createHelper");
        _ = Type(assembly, "Framework.Achievements.LocalAchievementsHelper").Methods.Single(m => m.IsConstructor && !m.IsStatic && !m.HasParameters);
    }
    static void ValidatePlatformTypes(AssemblyDefinition assembly)
    {
        _ = Type(assembly, "Steamworks.SteamRemoteStorage");
        _ = Type(assembly, "Steamworks.SteamUserStats");
        _ = Method(Type(assembly, "Steamworks.SteamAPI"), "RestartAppIfNecessary");
    }
    static void PatchGameAssembly(string path, string saveRoot)
    {
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(path));
        using var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { InMemory = true, AssemblyResolver = resolver });
        var module = assembly.MainModule;
        var method = Method(Type(assembly, "Framework.Managers.PersistentManager"), "GetPathAppSettings");
        method.Body = new MethodBody(method);
        var il = method.Body.GetILProcessor();
        var normal = il.Create(OpCodes.Ldstr, saveRoot);
        il.Append(il.Create(OpCodes.Ldarg_0)); il.Append(il.Create(OpCodes.Brtrue, normal));
        il.Append(il.Create(OpCodes.Ldstr, saveRoot)); il.Append(il.Create(OpCodes.Ret)); il.Append(normal);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldc_I4_2)); il.Append(il.Create(OpCodes.Newarr, module.TypeSystem.Char));
        il.Append(il.Create(OpCodes.Dup)); il.Append(il.Create(OpCodes.Ldc_I4_0)); il.Append(il.Create(OpCodes.Ldc_I4, (int)'/')); il.Append(il.Create(OpCodes.Stelem_I2));
        il.Append(il.Create(OpCodes.Dup)); il.Append(il.Create(OpCodes.Ldc_I4_1)); il.Append(il.Create(OpCodes.Ldc_I4, (int)'\\')); il.Append(il.Create(OpCodes.Stelem_I2));
        var trim = new MethodReference("TrimStart", module.TypeSystem.String, module.TypeSystem.String) { HasThis = true };
        trim.Parameters.Add(new ParameterDefinition(new ArrayType(module.TypeSystem.Char)));
        il.Append(il.Create(OpCodes.Callvirt, trim));
        var concat = new MethodReference("Concat", module.TypeSystem.String, module.TypeSystem.String);
        concat.Parameters.Add(new ParameterDefinition(module.TypeSystem.String)); concat.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        il.Append(il.Create(OpCodes.Call, concat)); il.Append(il.Create(OpCodes.Ret));
        var factory = Method(Type(assembly, "Framework.Managers.AchievementsManager"), "createHelper");
        factory.Body = new MethodBody(factory);
        factory.Body.Instructions.Add(Instruction.Create(OpCodes.Newobj, Type(assembly, "Framework.Achievements.LocalAchievementsHelper").Methods.Single(m => m.IsConstructor && !m.IsStatic && !m.HasParameters)));
        factory.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        WriteAssembly(assembly, path);
    }
    static void PatchPlatformAssembly(string path)
    {
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(path));
        using var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { InMemory = true, AssemblyResolver = resolver });
        foreach (var method in Type(assembly, "Steamworks.SteamRemoteStorage").Methods.Where(m => m.IsPublic && !m.IsConstructor)) DefaultReturn(method);
        foreach (var method in Type(assembly, "Steamworks.SteamUserStats").Methods.Where(m => m.IsPublic &&
            new[] { "Set", "Clear", "Store", "Reset", "Indicate", "Upload", "Update", "FindOrCreate" }.Any(prefix => m.Name.StartsWith(prefix, StringComparison.Ordinal)))) DefaultReturn(method);
        // Keep the copy in this slot: Steam's ownership-relaunch helper may launch
        // the normal installed executable. SteamAPI.Init itself is left intact.
        DefaultReturn(Method(Type(assembly, "Steamworks.SteamAPI"), "RestartAppIfNecessary"));
        WriteAssembly(assembly, path);
    }
    static void DefaultReturn(MethodDefinition method)
    {
        method.IsPInvokeImpl = false;
        method.ImplAttributes &= ~(MethodImplAttributes.InternalCall | MethodImplAttributes.Native | MethodImplAttributes.Runtime);
        method.Body = new MethodBody(method) { InitLocals = true };
        if (method.ReturnType.FullName != "System.Void")
        {
            if (!method.ReturnType.IsValueType) method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldnull));
            else
            {
                var value = new VariableDefinition(method.ReturnType); method.Body.Variables.Add(value);
                method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldloca, value));
                method.Body.Instructions.Add(Instruction.Create(OpCodes.Initobj, method.ReturnType));
                method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, value));
            }
        }
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
    }
    static void WriteAssembly(AssemblyDefinition assembly, string path)
    {
        var temporary = path + ".qa-tmp";
        assembly.Write(temporary); File.Move(temporary, path, true);
    }

    sealed class UnityBundle : IDisposable
    {
        sealed record Block(int Uncompressed, int Compressed, ushort Flags, int InfoOffset, long DataOffset, long LogicalOffset);
        sealed record Node(long Offset, long Size, string Path);
        readonly FileStream stream;
        readonly byte[] header, info;
        readonly List<Block> blocks = new();
        readonly Node managers;
        readonly int sizeField, compressedInfoField, flagsField;
        readonly uint flags;
        readonly long dataOffset;
        public UnityBundle(string path)
        {
            stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.UTF8, true);
            if (CString(reader) != "UnityFS" || U32(reader) != 6) throw new InvalidDataException("Only the observed UnityFS version 6 layout is supported.");
            _ = CString(reader); _ = CString(reader); sizeField = (int)stream.Position;
            if (U64(reader) != stream.Length) throw new InvalidDataException("Bundle size does not match its header.");
            compressedInfoField = (int)stream.Position; var compressed = checked((int)U32(reader)); var uncompressed = checked((int)U32(reader));
            flagsField = (int)stream.Position; flags = U32(reader);
            if ((flags & 0x280) != 0 || (flags & 0x40) == 0) throw new InvalidDataException("Unsupported UnityFS block-info placement/alignment.");
            var headerLength = (int)stream.Position;
            stream.Position = 0; header = reader.ReadBytes(headerLength);
            info = Decode(reader.ReadBytes(compressed), uncompressed, (int)(flags & 63)); dataOffset = stream.Position;
            if (info.Take(16).Any(b => b != 0)) throw new InvalidDataException("A nonzero bundle hash requires a hash-aware serializer.");
            using var meta = new BinaryReader(new MemoryStream(info)); meta.BaseStream.Position = 16;
            var count = U32(meta); long physical = dataOffset, logical = 0;
            for (var i = 0; i < count; i++)
            {
                var at = (int)meta.BaseStream.Position;
                var unpacked = checked((int)U32(meta)); var packed = checked((int)U32(meta)); var blockFlags = U16(meta);
                blocks.Add(new Block(unpacked, packed, blockFlags, at, physical, logical)); physical += packed; logical += unpacked;
            }
            var nodes = new List<Node>(); var nodeCount = U32(meta);
            for (var i = 0; i < nodeCount; i++) { var at = U64(meta); var length = U64(meta); _ = U32(meta); nodes.Add(new Node(at, length, CString(meta))); }
            managers = nodes.Single(n => n.Path == "globalgamemanagers");
            if (physical != stream.Length || managers.Size > 16 * 1024 * 1024) throw new InvalidDataException("Unexpected bundle bounds.");
        }
        byte[] ReadBlock(Block block)
        {
            stream.Position = block.DataOffset; var bytes = new byte[block.Compressed]; stream.ReadExactly(bytes);
            return Decode(bytes, block.Uncompressed, block.Flags & 63);
        }
        byte[] ReadManagers()
        {
            var bytes = new byte[checked((int)managers.Size)];
            foreach (var block in blocks.Where(b => b.LogicalOffset + b.Uncompressed > managers.Offset && b.LogicalOffset < managers.Offset + managers.Size))
            {
                var content = ReadBlock(block);
                var start = Math.Max(block.LogicalOffset, managers.Offset); var end = Math.Min(block.LogicalOffset + block.Uncompressed, managers.Offset + managers.Size);
                Array.Copy(content, start - block.LogicalOffset, bytes, start - managers.Offset, end - start);
            }
            return bytes;
        }
        public int FindProduct(string expected) => ProductOffset(ReadManagers(), expected);
        public void WriteProductCopy(string destination, string product)
        {
            var content = ReadManagers(); var position = ProductOffset(content, "Blasphemous");
            var replacement = Encoding.UTF8.GetBytes(product);
            if (replacement.Length != 11) throw new InvalidDataException("Product replacement must preserve the serialized string length.");
            var globalPosition = managers.Offset + position;
            var block = blocks.Single(b => b.LogicalOffset <= globalPosition && b.LogicalOffset + b.Uncompressed >= globalPosition + replacement.Length);
            var changed = ReadBlock(block); replacement.CopyTo(changed, checked((int)(globalPosition - block.LogicalOffset)));
            var newInfo = (byte[])info.Clone();
            BinaryPrimitives.WriteUInt32BigEndian(newInfo.AsSpan(block.InfoOffset + 4), (uint)changed.Length);
            BinaryPrimitives.WriteUInt16BigEndian(newInfo.AsSpan(block.InfoOffset + 8), (ushort)(block.Flags & ~63));
            var newHeader = (byte[])header.Clone();
            var newLength = stream.Length + newInfo.Length - (dataOffset - header.Length) + changed.Length - block.Compressed;
            BinaryPrimitives.WriteInt64BigEndian(newHeader.AsSpan(sizeField), newLength);
            BinaryPrimitives.WriteUInt32BigEndian(newHeader.AsSpan(compressedInfoField), (uint)newInfo.Length);
            BinaryPrimitives.WriteUInt32BigEndian(newHeader.AsSpan(flagsField), flags & ~63u);
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write))
            {
                output.Write(newHeader); output.Write(newInfo);
                foreach (var item in blocks)
                {
                    if (item == block) output.Write(changed);
                    else { stream.Position = item.DataOffset; Copy(stream, output, item.Compressed); }
                }
                if (output.Length != newLength) throw new IOException("Repacked bundle length mismatch.");
            }
            using var check = new UnityBundle(destination);
            check.FindProduct(product);
            var actual = check.ReadManagers(); replacement.CopyTo(content, position);
            if (!actual.SequenceEqual(content)) throw new IOException("Serialized-file mutation exceeded the product string.");
        }
        static int ProductOffset(byte[] bytes, string expected)
        {
            using var reader = new BinaryReader(new MemoryStream(bytes));
            _ = U32(reader); if (U32(reader) != bytes.Length || U32(reader) != 17) throw new InvalidDataException("Expected a SerializedFile v17.");
            var dataStart = U32(reader); if (reader.ReadByte() != 0) throw new InvalidDataException("Expected little-endian serialized objects.");
            reader.ReadBytes(3); _ = CString(reader); _ = reader.ReadInt32();
            if (reader.ReadBoolean()) throw new InvalidDataException("Unexpected type-tree-bearing PlayerSettings asset.");
            var typeCount = reader.ReadInt32(); var types = new List<int>();
            for (var i = 0; i < typeCount; i++) { var type = reader.ReadInt32(); types.Add(type); _ = reader.ReadByte(); _ = reader.ReadInt16(); if (type == 114) reader.ReadBytes(16); reader.ReadBytes(16); }
            var count = reader.ReadInt32(); var candidates = new List<(int Start, int Size)>();
            for (var i = 0; i < count; i++)
            {
                while (reader.BaseStream.Position % 4 != 0) reader.ReadByte();
                _ = reader.ReadInt64(); var at = checked((int)(reader.ReadUInt32() + dataStart)); var length = checked((int)reader.ReadUInt32()); var type = reader.ReadInt32();
                if (types[type] == 129) candidates.Add((at, length));
            }
            var settings = candidates.Single();
            // PlayerSettings company's serialized string followed by productName.
            var company = Encoding.UTF8.GetBytes("TheGameKitchen"); var positions = new List<int>();
            for (var i = settings.Start; i <= settings.Start + settings.Size - company.Length; i++)
                if (bytes.AsSpan(i, company.Length).SequenceEqual(company) && i >= 4 && BitConverter.ToInt32(bytes, i - 4) == company.Length) positions.Add(i);
            var companyPosition = positions.Single(); var productLengthPosition = (companyPosition + company.Length + 3) & ~3;
            var productPosition = productLengthPosition + 4;
            if (BitConverter.ToInt32(bytes, productLengthPosition) != 11 || Encoding.UTF8.GetString(bytes, productPosition, 11) != expected)
                throw new InvalidDataException("Serialized PlayerSettings product identity differs from the expected value.");
            return productPosition;
        }
        static byte[] Decode(byte[] input, int size, int codec)
        {
            if (codec == 0) { if (input.Length != size) throw new InvalidDataException("Uncompressed block length mismatch."); return input; }
            if (codec != 2 && codec != 3) throw new InvalidDataException("Unsupported bundle compression.");
            var output = new byte[size]; var ip = 0; var op = 0;
            while (ip < input.Length)
            {
                var token = input[ip++]; var literal = token >> 4;
                if (literal == 15) { int n; do { n = input[ip++]; literal += n; } while (n == 255); }
                Array.Copy(input, ip, output, op, literal); ip += literal; op += literal;
                if (ip == input.Length) break;
                var distance = input[ip] | (input[ip + 1] << 8); ip += 2;
                var match = token & 15; if (match == 15) { int n; do { n = input[ip++]; match += n; } while (n == 255); } match += 4;
                if (distance <= 0 || distance > op) throw new InvalidDataException("Invalid LZ4 distance.");
                for (var j = 0; j < match; j++) { output[op] = output[op - distance]; op++; }
            }
            if (op != size) throw new InvalidDataException("LZ4 decoded size mismatch.");
            return output;
        }
        static void Copy(Stream input, Stream output, int count)
        {
            var buffer = new byte[Math.Min(count, 128 * 1024)];
            while (count > 0) { var read = input.Read(buffer, 0, Math.Min(buffer.Length, count)); if (read == 0) throw new EndOfStreamException(); output.Write(buffer, 0, read); count -= read; }
        }
        static string CString(BinaryReader reader) { var result = new List<byte>(); byte value; while ((value = reader.ReadByte()) != 0) result.Add(value); return Encoding.UTF8.GetString(result.ToArray()); }
        static ushort U16(BinaryReader reader) => BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
        static uint U32(BinaryReader reader) => BinaryPrimitives.ReadUInt32BigEndian(reader.ReadBytes(4));
        static long U64(BinaryReader reader) => BinaryPrimitives.ReadInt64BigEndian(reader.ReadBytes(8));
        public void Dispose() => stream.Dispose();
    }
}
