using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodBody = Mono.Cecil.Cil.MethodBody;
using MethodImplAttributes = Mono.Cecil.MethodImplAttributes;

internal static class Program
{
    const string SupportedExeSha256 = "3FF1A014FD0366747FB6D283C9494A5A8CFF9BC19239FE97B9DC12D831369477";
    const string ProductName = "BlasCoopLAN";
    const string DependenciesLink = "https://github.com/BrandenEK/Blasphemous.Modding.Installer";

    static int Main(string[] args)
    {
        try
        {
            var options = Parse(args);
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This player-copy builder supports the verified Windows build only.");
            var source = Path.GetFullPath(options["--game-directory"]).TrimEnd(Path.DirectorySeparatorChar);
            var destination = Path.GetFullPath(options["--destination"]).TrimEnd(Path.DirectorySeparatorChar);
            var manifestPath = Path.GetFullPath(options["--payload-manifest"]);
            if (IsWithin(destination, source) || IsWithin(source, destination) || source.Equals(destination, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The co-op destination must be separate from the original game directory.");
            RejectReparsePoints(source);
            RejectReparsePoints(destination);
            if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
                throw new InvalidOperationException("Destination is not empty. Choose a new directory; existing installs and saves are never overwritten.");
            if (File.Exists(destination)) throw new InvalidOperationException("Destination is an existing file.");
            if (Hash(Path.Combine(source, "Blasphemous.exe")) != SupportedExeSha256)
                throw new InvalidOperationException("Game executable does not match the supported, recorded Blasphemous Windows build.");
            var payload = ReadPayload(manifestPath);
            CheckDependencies(source);
            var sourceData = Path.Combine(source, "Blasphemous_Data");
            var gameAssembly = Path.Combine(sourceData, "Managed", "Assembly-CSharp.dll");
            var platformAssembly = Path.Combine(sourceData, "Managed", "Assembly-CSharp-firstpass.dll");
            using (var bundle = new UnityBundle(Path.Combine(sourceData, "data.unity3d"))) bundle.FindProduct("Blasphemous");
            using (var assembly = AssemblyDefinition.ReadAssembly(gameAssembly)) _ = SavePathMethod(assembly);
            using (var assembly = AssemblyDefinition.ReadAssembly(platformAssembly)) ValidatePlatform(assembly);
            // Enumerate and validate every copied source path before creating the destination.
            var copyPlan = BuildCopyPlan(source);
            var sourceHashes = new Dictionary<string, string> {
                ["Blasphemous.exe"] = SupportedExeSha256,
                ["Blasphemous_Data/data.unity3d"] = Hash(Path.Combine(sourceData, "data.unity3d")),
                ["Blasphemous_Data/Managed/Assembly-CSharp.dll"] = Hash(gameAssembly),
                ["Blasphemous_Data/Managed/Assembly-CSharp-firstpass.dll"] = Hash(platformAssembly),
                ["Blasphemous_Data/app.info"] = Hash(Path.Combine(sourceData, "app.info"))
            };
            Console.WriteLine("Creating a physical co-op copy in " + destination);
            Directory.CreateDirectory(destination);
            foreach (var item in copyPlan)
            {
                var target = Path.Combine(destination, item.Relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(item.Source, target, false);
            }
            var profile = Path.Combine(destination, "coop-profile");
            var backups = Path.Combine(profile, "setup-backups");
            var saves = Path.Combine(destination, "saves") + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(profile); Directory.CreateDirectory(saves);
            var bundlePath = Path.Combine(destination, "Blasphemous_Data", "data.unity3d");
            var gamePath = Path.Combine(destination, "Blasphemous_Data", "Managed", "Assembly-CSharp.dll");
            var platformPath = Path.Combine(destination, "Blasphemous_Data", "Managed", "Assembly-CSharp-firstpass.dll");
            var records = new List<FileRecord>();
            foreach (var file in new[] { bundlePath, gamePath, platformPath, Path.Combine(destination, "Blasphemous_Data", "app.info") })
            {
                RequireSingleLink(file);
                var relative = Path.GetRelativePath(destination, file);
                var backup = Path.Combine(backups, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!); File.Copy(file, backup, false);
                var before = Hash(file);
                if (Hash(backup) != before) throw new IOException("Copy backup failed verification.");
                records.Add(new FileRecord { Path = relative.Replace('\\', '/'), BeforeSha256 = before });
            }
            using (var bundle = new UnityBundle(bundlePath)) bundle.WriteProductCopy(bundlePath + ".coop-tmp", ProductName);
            File.Move(bundlePath + ".coop-tmp", bundlePath, true);
            PatchSavePath(gamePath, saves);
            PatchPrivateProfilePlatform(platformPath);
            Write(Path.Combine(destination, "Blasphemous_Data", "app.info"), "TheGameKitchen\n" + ProductName + "\n");
            Write(Path.Combine(destination, "doorstop_config.ini"), "[UnityDoorstop]\nenabled=true\ntargetAssembly=BepInEx\\core\\BepInEx.Preloader.dll\nredirectOutputLog=false\nignoreDisableSwitch=false\n[MultiFolderLoader]\nbaseDir=Modding\\plugins\n");
            Write(Path.Combine(destination, "steam_appid.txt"), "774361\n");
            InstallPayload(payload, destination);
            Write(Path.Combine(destination, "Modding", "config", "Multiplayer.cfg"), JsonSerializer.Serialize(new {
                notificationDisplaySeconds = 4f, displayNametags = true, displayOwnNametag = true,
                showPlayersOnMap = true, showOtherTeamOnMap = false, enablePvP = false,
                enableFriendlyFire = false, preferredEnemyHost = "",
                syncSettings = new { inventoryItems = true, playerStats = true, swordSkills = true, worldState = true, mapCells = true }
            }, new JsonSerializerOptions { WriteIndented = true }));
            Write(Path.Combine(destination, "Play-Coop.cmd"), "@echo off\r\npushd \"%~dp0\"\r\nstart \"\" \"%~dp0Blasphemous.exe\" -logFile \"%~dp0coop-profile\\Player.log\"\r\npopd\r\n");
            foreach (var record in records) record.AfterSha256 = Hash(Path.Combine(destination, record.Path.Replace('/', Path.DirectorySeparatorChar)));
            foreach (var pair in sourceHashes)
                if (Hash(Path.Combine(source, pair.Key.Replace('/', Path.DirectorySeparatorChar))) != pair.Value)
                    throw new IOException("An original file changed during setup: " + pair.Key);
            var result = new {
                schemaVersion = 1, productName = ProductName, saveRoot = saves,
                clientVersion = payload.ClientVersion, protocol = payload.Protocol,
                payloadArchiveSha256 = payload.Sha256,
                originalGameFilesUnchanged = true, steamAchievements = "preserved",
                cloudStorage = "disabled for this separate local profile", files = records
            };
            Write(Path.Combine(profile, "setup-manifest.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            Write(Path.Combine(destination, "COOP-README.txt"), "Blasphemous separate co-op copy\n\nLaunch Play-Coop.cmd. Keep Steam running with your owned game.\nSaves are private to this copy under saves/. Normal game saves were not imported.\nSteam achievements remain enabled. Steam cloud storage is disabled for this private profile.\nEvery player and the server must use protocol " + payload.Protocol + ".\nUse different player names, the same team, and a reachable host address.\nYour original game installation was not changed.\nTo remove this profile later, remove this copy after preserving any saves you want.\n");
            Console.WriteLine("Prepared client " + payload.ClientVersion + ", protocol " + payload.Protocol + ". Steam achievements preserved; original game unchanged.");
            Console.WriteLine("Launch manually: " + Path.Combine(destination, "Play-Coop.cmd"));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Co-op setup failed: " + error.Message);
            Console.Error.WriteLine("The original install was not written. A partially created destination is left in place for inspection.");
            return 1;
        }
    }

    sealed record CopyItem(string Source, string Relative);
    sealed record Payload(string Archive, string Sha256, string ClientVersion, int Protocol);
    sealed class FileRecord { public string Path { get; set; } = ""; public string BeforeSha256 { get; set; } = ""; public string AfterSha256 { get; set; } = ""; }
    static Dictionary<string, string> Parse(string[] args)
    {
        var valid = new[] { "--game-directory", "--destination", "--payload-manifest" };
        if (args.Length != 6) throw new ArgumentException("Usage: CoopPlaySetup --game-directory <owned modded game> --destination <new empty directory> --payload-manifest <manifest.json>");
        var result = new Dictionary<string, string>();
        for (var i = 0; i < args.Length; i += 2)
            if (!valid.Contains(args[i]) || !result.TryAdd(args[i], args[i + 1])) throw new ArgumentException("Unknown or duplicate setup option.");
        if (valid.Any(v => !result.ContainsKey(v))) throw new ArgumentException("All three setup options are required.");
        return result;
    }
    static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    static void Write(string path, string text) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text, new UTF8Encoding(false)); }
    static bool IsWithin(string path, string root) => path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    static void RejectReparsePoints(string path)
    {
        for (FileSystemInfo? entry = File.Exists(path) ? new FileInfo(path) : new DirectoryInfo(path); entry != null; entry = Directory.GetParent(entry.FullName))
            if (entry.Exists && (entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Reparse-point paths are refused: " + entry.FullName);
    }
    static void CheckDependencies(string source)
    {
        var required = new[] { "UnityPlayer.dll", "winhttp.dll", "BepInEx/core/BepInEx.dll", "BepInEx/core/BepInEx.Preloader.dll", "BepInEx/patchers/BepInEx.MultiFolderLoader.dll", "Modding/plugins/ModdingAPI.dll", "Modding/plugins/CheatConsole.dll" };
        foreach (var relative in required)
            if (!File.Exists(Path.Combine(source, relative.Replace('/', Path.DirectorySeparatorChar))))
                throw new InvalidOperationException("Missing mod-loader dependency " + relative + ". Install the official Modding API and Cheat Console base into a separate source copy, then retry. Official installer: " + DependenciesLink);
        var bepinex = AssemblyName.GetAssemblyName(Path.Combine(source, "BepInEx", "core", "BepInEx.dll")).Version!;
        var api = AssemblyName.GetAssemblyName(Path.Combine(source, "Modding", "plugins", "ModdingAPI.dll")).Version!;
        var console = AssemblyName.GetAssemblyName(Path.Combine(source, "Modding", "plugins", "CheatConsole.dll")).Version!;
        if (bepinex.Major != 5 || api < new Version(3, 0, 0) || console < new Version(1, 1, 0))
            throw new InvalidOperationException("Requires BepInEx 5, Modding API >=3.0.0 and Cheat Console >=1.1.0. Official installer: " + DependenciesLink);
    }
    static List<CopyItem> BuildCopyPlan(string source)
    {
        var result = new List<CopyItem>();
        void Add(string file)
        {
            RejectReparsePoints(file);
            result.Add(new CopyItem(file, Path.GetRelativePath(source, file)));
        }
        foreach (var file in new[] { "Blasphemous.exe", "UnityPlayer.dll", "Galaxy64.dll", "GalaxyCSharpGlue.dll", "winhttp.dll" })
            if (File.Exists(Path.Combine(source, file))) Add(Path.Combine(source, file));
        void Tree(string root)
        {
            RejectReparsePoints(root);
            foreach (var file in Directory.EnumerateFiles(root)) Add(file);
            foreach (var folder in Directory.EnumerateDirectories(root)) Tree(folder);
        }
        Tree(Path.Combine(source, "Blasphemous_Data"));
        Tree(Path.Combine(source, "BepInEx", "core"));
        Add(Path.Combine(source, "BepInEx", "patchers", "BepInEx.MultiFolderLoader.dll"));
        Add(Path.Combine(source, "Modding", "plugins", "ModdingAPI.dll"));
        Add(Path.Combine(source, "Modding", "plugins", "CheatConsole.dll"));
        foreach (var baseResource in new[] { "Modding API.txt", "Cheat Console.txt", "ModdingAPI.txt", "CheatConsole.txt" })
        {
            var path = Path.Combine(source, "Modding", "localization", baseResource);
            if (File.Exists(path)) Add(path);
        }
        return result;
    }
    static Payload ReadPayload(string manifestPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath)); var root = doc.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidDataException("Unsupported payload manifest schema.");
        var relative = root.GetProperty("clientArchive").GetString()!;
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Split('/', '\\').Any(p => p is ".." or "." || p.Contains(':')))
            throw new InvalidDataException("The payload archive must be a relative path inside the manifest's directory.");
        var archive = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath)!, relative));
        RejectReparsePoints(archive);
        var sha = root.GetProperty("clientSha256").GetString()?.ToUpperInvariant() ?? "";
        if (sha.Length != 64 || !sha.All(Uri.IsHexDigit) || Hash(archive) != sha) throw new InvalidDataException("Client archive SHA-256 does not match the payload manifest.");
        var version = root.GetProperty("clientVersion").GetString()!;
        if (!Version.TryParse(version, out _)) throw new InvalidDataException("Client version must be an assembly-style numeric version.");
        var protocol = root.GetProperty("protocol").GetInt32();
        if (protocol is < 1 or > 255) throw new InvalidDataException("Invalid client protocol.");
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count > 256 || zip.Entries.Sum(e => e.Length) > 128L * 1024 * 1024) throw new InvalidDataException("Client archive exceeds expected mod package bounds.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries.Where(e => !e.FullName.EndsWith('/')))
        {
            ValidateEntry(entry);
            if (!names.Add(entry.FullName)) throw new InvalidDataException("Duplicate client payload path: " + entry.FullName);
        }
        foreach (var required in new[] { "plugins/BlasClient.dll", "data/Blasphemous.Multiplayer.Common.dll", "data/SimpleTCP.dll" })
            if (zip.Entries.Count(e => e.FullName == required) != 1) throw new InvalidDataException("Missing/duplicate client payload file: " + required);
        using (var entryStream = zip.GetEntry("plugins/BlasClient.dll")!.Open())
        using (var memory = new MemoryStream())
        {
            entryStream.CopyTo(memory); memory.Position = 0;
            using var client = AssemblyDefinition.ReadAssembly(memory);
            var expected = Version.Parse(version);
            if (client.Name.Version.Major != expected.Major || client.Name.Version.Minor != expected.Minor || client.Name.Version.Build != expected.Build)
                throw new InvalidDataException("Client assembly version differs from the manifest.");
        }
        using (var entryStream = zip.GetEntry("data/Blasphemous.Multiplayer.Common.dll")!.Open())
        using (var memory = new MemoryStream())
        {
            entryStream.CopyTo(memory); memory.Position = 0;
            using var common = AssemblyDefinition.ReadAssembly(memory);
            var field = Type(common, "Blasphemous.Multiplayer.Common.Protocol").Fields.Single(f => f.Name == "VERSION");
            if (!field.HasConstant || Convert.ToInt32(field.Constant) != protocol) throw new InvalidDataException("Client protocol differs from the manifest.");
        }
        return new Payload(archive, sha, version, protocol);
    }
    static void ValidateEntry(ZipArchiveEntry entry)
    {
        var path = entry.FullName;
        if (path.Contains('\\') || path.StartsWith('/') || path.Split('/').Any(p => p is "" or "." or ".." || p.Contains(':')))
            throw new InvalidDataException("Unsafe client archive entry: " + path);
        var allowed = path is "plugins/BlasClient.dll" or "data/Blasphemous.Multiplayer.Common.dll" or "data/SimpleTCP.dll" or "localization/Multiplayer.txt" ||
            (path.StartsWith("data/Multiplayer/", StringComparison.Ordinal) && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        if (!allowed) throw new InvalidDataException("Unexpected client payload file (game assets, other plugins and QA files are not accepted): " + path);
    }
    static void InstallPayload(Payload payload, string destination)
    {
        using var file = new FileStream(payload.Archive, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (Convert.ToHexString(SHA256.HashData(file)) != payload.Sha256) throw new InvalidDataException("Client archive changed during setup.");
        file.Position = 0;
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries.Where(e => !e.FullName.EndsWith('/')))
        {
            var target = Path.Combine(destination, "Modding", entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var source = entry.Open(); using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write); source.CopyTo(output);
        }
    }
    static TypeDefinition Type(AssemblyDefinition assembly, string name) => assembly.MainModule.Types.Single(t => t.FullName == name);
    static MethodDefinition Method(TypeDefinition type, string name) => type.Methods.Single(m => m.Name == name);
    static MethodDefinition SavePathMethod(AssemblyDefinition assembly)
    {
        var method = Method(Type(assembly, "Framework.Managers.PersistentManager"), "GetPathAppSettings");
        if (!method.IsStatic || method.Parameters.Count != 1 || method.ReturnType.FullName != "System.String") throw new InvalidDataException("Unsupported save-path API.");
        return method;
    }
    static void ValidatePlatform(AssemblyDefinition assembly)
    {
        _ = Type(assembly, "Steamworks.SteamRemoteStorage"); _ = Method(Type(assembly, "Steamworks.SteamAPI"), "RestartAppIfNecessary");
    }
    static void PatchSavePath(string path, string saveRoot)
    {
        using var resolver = new DefaultAssemblyResolver(); resolver.AddSearchDirectory(Path.GetDirectoryName(path));
        using var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { InMemory = true, AssemblyResolver = resolver });
        var factoryBefore = Fingerprint(Method(Type(assembly, "Framework.Managers.AchievementsManager"), "createHelper"));
        var module = assembly.MainModule; var method = SavePathMethod(assembly); method.Body = new MethodBody(method); var il = method.Body.GetILProcessor();
        var normal = il.Create(OpCodes.Ldstr, saveRoot);
        il.Append(il.Create(OpCodes.Ldarg_0)); il.Append(il.Create(OpCodes.Brtrue, normal)); il.Append(il.Create(OpCodes.Ldstr, saveRoot)); il.Append(il.Create(OpCodes.Ret)); il.Append(normal); il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldc_I4_2)); il.Append(il.Create(OpCodes.Newarr, module.TypeSystem.Char));
        il.Append(il.Create(OpCodes.Dup)); il.Append(il.Create(OpCodes.Ldc_I4_0)); il.Append(il.Create(OpCodes.Ldc_I4, (int)'/')); il.Append(il.Create(OpCodes.Stelem_I2));
        il.Append(il.Create(OpCodes.Dup)); il.Append(il.Create(OpCodes.Ldc_I4_1)); il.Append(il.Create(OpCodes.Ldc_I4, (int)'\\')); il.Append(il.Create(OpCodes.Stelem_I2));
        var trim = new MethodReference("TrimStart", module.TypeSystem.String, module.TypeSystem.String) { HasThis = true }; trim.Parameters.Add(new ParameterDefinition(new ArrayType(module.TypeSystem.Char))); il.Append(il.Create(OpCodes.Callvirt, trim));
        var concat = new MethodReference("Concat", module.TypeSystem.String, module.TypeSystem.String); concat.Parameters.Add(new ParameterDefinition(module.TypeSystem.String)); concat.Parameters.Add(new ParameterDefinition(module.TypeSystem.String)); il.Append(il.Create(OpCodes.Call, concat)); il.Append(il.Create(OpCodes.Ret));
        if (Fingerprint(Method(Type(assembly, "Framework.Managers.AchievementsManager"), "createHelper")) != factoryBefore) throw new InvalidOperationException("Achievement factory changed unexpectedly.");
        SaveAssembly(assembly, path);
    }
    static void PatchPrivateProfilePlatform(string path)
    {
        using var resolver = new DefaultAssemblyResolver(); resolver.AddSearchDirectory(Path.GetDirectoryName(path));
        using var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { InMemory = true, AssemblyResolver = resolver });
        var stats = Type(assembly, "Steamworks.SteamUserStats").Methods.ToDictionary(m => m.FullName, Fingerprint);
        foreach (var method in Type(assembly, "Steamworks.SteamRemoteStorage").Methods.Where(m => m.IsPublic && !m.IsConstructor)) DefaultReturn(method);
        DefaultReturn(Method(Type(assembly, "Steamworks.SteamAPI"), "RestartAppIfNecessary"));
        if (Type(assembly, "Steamworks.SteamUserStats").Methods.Any(m => stats[m.FullName] != Fingerprint(m))) throw new InvalidOperationException("Steam achievement/stat methods changed unexpectedly.");
        SaveAssembly(assembly, path);
    }
    static string Fingerprint(MethodDefinition method) => method.HasBody ? string.Join("\n", method.Body.Instructions.Select(i => i.OpCode.Name + " " + i.Operand)) : "<external>";
    static void DefaultReturn(MethodDefinition method)
    {
        method.IsPInvokeImpl = false; method.ImplAttributes &= ~(MethodImplAttributes.InternalCall | MethodImplAttributes.Native | MethodImplAttributes.Runtime); method.Body = new MethodBody(method) { InitLocals = true };
        if (method.ReturnType.FullName != "System.Void")
        {
            if (!method.ReturnType.IsValueType) method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldnull));
            else { var value = new VariableDefinition(method.ReturnType); method.Body.Variables.Add(value); method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldloca, value)); method.Body.Instructions.Add(Instruction.Create(OpCodes.Initobj, method.ReturnType)); method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, value)); }
        }
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
    }
    static void SaveAssembly(AssemblyDefinition assembly, string path) { assembly.Write(path + ".coop-tmp"); File.Move(path + ".coop-tmp", path, true); }
    [StructLayout(LayoutKind.Sequential)] struct FileInformation { public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written; public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow; }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation info);
    static void RequireSingleLink(string path) { using var stream = File.OpenRead(path); if (!GetFileInformationByHandle(stream.SafeFileHandle, out var info) || info.Links != 1) throw new IOException("Copied target is not a verified physical file: " + path); }
}
