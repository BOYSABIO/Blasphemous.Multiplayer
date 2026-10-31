// Derived from the verified CoopQa.Isolation UnityFS parser; kept local so QA behavior is unchanged.
using System.Buffers.Binary;
using System.Text;

internal sealed class UnityBundle : IDisposable
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
