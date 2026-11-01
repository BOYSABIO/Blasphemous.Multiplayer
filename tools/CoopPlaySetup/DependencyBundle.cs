using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;

// The bundle is local input, never a script or an installer to execute. The four
// accepted binaries are pinned official releases matching the runtime QA loader.
internal sealed class DependencyBundle
{
    sealed record Pin(string Sha256, string Url, string Assembly, Version Version, string? Entry);
    sealed record Asset(string Id, string Path, Pin Pin);
    sealed record Notice(string Path, string Relative, string Sha256, string Url);
    static readonly Dictionary<string, Pin> Pins = new(StringComparer.Ordinal) {
        ["bepinex"] = new("2AF69FE0AAF821E95C4CD3E4D53860E667C54648F97DCA6F971A5BCD3C22AA34", "https://github.com/BepInEx/BepInEx/releases/download/v5.4.21/BepInEx_x64_5.4.21.0.zip", "BepInEx", new(5,4,21,0), "BepInEx/core/BepInEx.dll"),
        ["multifolder"] = new("6FB2CB30CB0E25BA3FDAA7F6E4CF9FDC860BB4FD2574DEDF690B5FE1C2248F6C", "https://github.com/BepInEx/BepInEx.MultiFolderLoader/releases/download/v1.3.1/BepInEx.MultiFolderLoader.dll", "BepInEx.MultiFolderLoader", new(1,3,1,0), null),
        ["api"] = new("B74FE3900B563483D090FD7F01939FE7D60BA5711E83D7996396A274E7156FF7", "https://github.com/BrandenEK/Blasphemous.ModdingAPI/releases/download/3.0.1/ModdingAPI.zip", "ModdingAPI", new(3,0,1,0), "plugins/ModdingAPI.dll"),
        ["console"] = new("2280E5B4FD82A88395AAE5F841A4B64C47CF24A22C0603F02EFB220662067DB8", "https://github.com/BrandenEK/Blasphemous.CheatConsole/releases/download/1.1.0/CheatConsole.zip", "CheatConsole", new(1,1,0,0), "plugins/CheatConsole.dll")
    };
    static readonly Dictionary<string, (string Sha256, string Url)> NoticePins = new(StringComparer.Ordinal) {
        ["BepInEx-LICENSE.txt"] = ("E6E534EF6F4347B6449407EE046A3D09CB0174C6F688C996AD0BED94B74B3933", "https://raw.githubusercontent.com/BepInEx/BepInEx/v5.4.21/LICENSE"),
        ["MultiFolderLoader-LICENSE.txt"] = ("00DE20316BB5F2B227A7219384785E0970AB0C800DAF374C1962E370C33497DE", "https://raw.githubusercontent.com/BepInEx/BepInEx.MultiFolderLoader/v1.3.1/LICENSE"),
        ["ModdingAPI-LICENSE.md"] = ("436CAB80543D96500519E67477D0EC36530CC6B7F8F076333386D8030EE099C6", "https://raw.githubusercontent.com/BrandenEK/Blasphemous.ModdingAPI/3.0.1/docs/LICENSE.md"),
        ["CheatConsole-LICENSE.md"] = ("D02BBE8E956B5A4F39CAEE418474210B5F4E13D37F203D4B543ABCAA1170BA7B", "https://raw.githubusercontent.com/BrandenEK/Blasphemous.CheatConsole/1.1.0/docs/LICENSE.md"),
        ["BepInEx-5.4.21-source.zip"] = ("62F4EEF48415E96F08778F939B10D6ACDC8744AB217518DB608D89DBAF15F08A", "https://github.com/BepInEx/BepInEx/archive/refs/tags/v5.4.21.zip")
    };
    readonly List<Asset> assets = new();
    readonly List<Notice> notices = new();
    public object Identity => assets.Select(a => new { id = a.Id, sha256 = a.Pin.Sha256, url = a.Pin.Url }).ToArray();

    public static DependencyBundle Read(string manifest)
    {
        manifest = Path.GetFullPath(manifest);
        RejectReparse(manifest);
        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidDataException("Unsupported dependency manifest schema.");
        var bundle = new DependencyBundle(); var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in root.GetProperty("assets").EnumerateArray())
        {
            string id = item.GetProperty("id").GetString()!;
            if (!Pins.TryGetValue(id, out var pin) || !ids.Add(id)) throw new InvalidDataException("Unknown or duplicate dependency ID.");
            if (item.GetProperty("sha256").GetString()?.ToUpperInvariant() != pin.Sha256 || item.GetProperty("url").GetString() != pin.Url)
                throw new InvalidDataException("Dependency manifest differs from the pinned official release: " + id);
            string path = RelativeFile(manifest, item.GetProperty("file").GetString()!);
            using var file = Verified(path, pin.Sha256);
            if (pin.Entry == null) VerifyAssembly(file, pin);
            else
            {
                using var zip = new ZipArchive(file, ZipArchiveMode.Read, true);
                ValidateZip(zip, id);
                using var entry = zip.GetEntry(pin.Entry)!.Open(); using var memory = new MemoryStream(); entry.CopyTo(memory); memory.Position = 0;
                VerifyAssembly(memory, pin);
            }
            bundle.assets.Add(new(id, path, pin));
        }
        if (ids.Count != Pins.Count) throw new InvalidDataException("The dependency bundle needs all four pinned components.");
        var noticeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in root.GetProperty("notices").EnumerateArray())
        {
            string relative = item.GetProperty("file").GetString()!;
            if (!noticeNames.Add(relative)) throw new InvalidDataException("Duplicate dependency notice.");
            string path = RelativeFile(manifest, relative), sha = item.GetProperty("sha256").GetString()!.ToUpperInvariant();
            string url = item.GetProperty("url").GetString()!;
            if (!NoticePins.TryGetValue(relative, out var pin) || sha != pin.Sha256 || url != pin.Url)
                throw new InvalidDataException("Dependency notice differs from the pinned release provenance: " + relative);
            using (Verified(path, sha)) { }
            bundle.notices.Add(new(path, relative, sha, url));
        }
        foreach (var required in NoticePins.Keys)
            if (!noticeNames.Contains(required)) throw new InvalidDataException("Missing dependency license/source notice: " + required);
        return bundle;
    }

    public void Install(string destination)
    {
        foreach (var asset in assets)
        {
            using var file = Verified(asset.Path, asset.Pin.Sha256);
            if (asset.Id == "multifolder") { Copy(file, Path.Combine(destination, "BepInEx/patchers/BepInEx.MultiFolderLoader.dll")); continue; }
            using var zip = new ZipArchive(file, ZipArchiveMode.Read);
            ValidateZip(zip, asset.Id);
            foreach (var entry in zip.Entries.Where(e => !e.FullName.EndsWith('/')))
            {
                if (asset.Id == "bepinex" && !(entry.FullName.StartsWith("BepInEx/core/", StringComparison.Ordinal) || entry.FullName == "winhttp.dll")) continue;
                string relative = asset.Id == "bepinex" ? entry.FullName : "Modding/" + entry.FullName;
                using var content = entry.Open(); Copy(content, Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar)));
            }
        }
        foreach (var notice in notices)
        {
            using var file = Verified(notice.Path, notice.Sha256);
            Copy(file, Path.Combine(destination, "coop-profile/third-party", notice.Relative.Replace('/', Path.DirectorySeparatorChar)));
        }
        File.WriteAllText(Path.Combine(destination,"coop-profile/third-party/dependency-provenance.json"), JsonSerializer.Serialize(new { assets = Identity, notices = notices.Select(n => new { file = n.Relative, sha256 = n.Sha256, url = n.Url }) }, new JsonSerializerOptions { WriteIndented = true }));
    }

    static void ValidateZip(ZipArchive zip, string id)
    {
        if (zip.Entries.Count > 128 || zip.Entries.Sum(e => e.Length) > 64L * 1024 * 1024) throw new InvalidDataException("Dependency archive exceeds expected bounds.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            string path = entry.FullName.TrimEnd('/'); ValidateRelative(path);
            if (!names.Add(path)) throw new InvalidDataException("Duplicate dependency archive path.");
            if (entry.FullName.EndsWith('/')) continue;
            bool valid = id == "bepinex"
                ? path is "changelog.txt" or "doorstop_config.ini" or "winhttp.dll" || (path.StartsWith("BepInEx/core/", StringComparison.Ordinal) && path.Split('/').Length == 3 && (path.EndsWith(".dll", StringComparison.Ordinal) || path.EndsWith(".xml", StringComparison.Ordinal)))
                : path == Pins[id].Entry;
            if (!valid) throw new InvalidDataException("Unexpected dependency archive path: " + path);
        }
        if (zip.GetEntry(Pins[id].Entry!) == null || (id == "bepinex" && zip.GetEntry("winhttp.dll") == null)) throw new InvalidDataException("Dependency archive is incomplete.");
    }
    static void VerifyAssembly(Stream input, Pin pin)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(input);
        if (assembly.Name.Name != pin.Assembly || assembly.Name.Version != pin.Version) throw new InvalidDataException("Unexpected dependency assembly identity: " + pin.Assembly);
    }
    static FileStream Verified(string path, string sha)
    {
        var file = File.OpenRead(path);
        if (sha.Length != 64 || !sha.All(Uri.IsHexDigit) || Convert.ToHexString(SHA256.HashData(file)) != sha) { file.Dispose(); throw new InvalidDataException("Dependency SHA-256 mismatch: " + Path.GetFileName(path)); }
        file.Position = 0; return file;
    }
    static void ValidateRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.StartsWith('/') || path.Split('/').Any(p => p is "" or "." or ".." || p.Contains(':')))
            throw new InvalidDataException("Unsafe dependency path: " + path);
    }
    static string RelativeFile(string manifest, string relative)
    { ValidateRelative(relative); var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifest)!, relative)); RejectReparse(path); return path; }
    static void RejectReparse(string path)
    {
        for (FileSystemInfo? item = new FileInfo(path); item != null; item = Directory.GetParent(item.FullName))
            if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Dependency paths cannot be reparse points.");
    }
    static void Copy(Stream input, string destination)
    { Directory.CreateDirectory(Path.GetDirectoryName(destination)!); using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write); input.CopyTo(output); }
}
