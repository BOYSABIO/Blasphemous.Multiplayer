using System.Text;
using Blasphemous.Multiplayer.Common;
using Blasphemous.Multiplayer.Common.Combat;
using Blasphemous.Multiplayer.Common.Enums;

static partial class Program
{
    static int checks;
    static void Check(bool ok, string label)
    {
        if (!ok) throw new Exception(label);
        checks++;
    }
    static byte[] Frame(NetworkType type, byte[] body) => BitConverter.GetBytes((ushort)body.Length).Concat(new[] { (byte)type }).Concat(body).ToArray();
    static byte[] Named(string name, byte[] body)
    {
        byte[] text = Encoding.UTF8.GetBytes(name);
        return new[] { (byte)text.Length }.Concat(text).Concat(body).ToArray();
    }
    static bool Rejected(Action action)
    {
        try { action(); return false; }
        catch (IOException) { return true; }
        catch (InvalidDataException) { return true; }
        catch (ArgumentException) { return true; }
    }
    static void Main()
    {
        Check(Protocol.VERSION == 13, "current protocol");
        var state = new EnemyStateData { Id = 42, Type = "BO01|Boss", X = 10.5f, Y = -3.25f, Life = 80,
            Alive = true, FacingRight = false, AnimationHash = -17001, AnimationTime = 1.75f };
        byte[] wrapped = Named("Höst", state.Encode(true));
        var decoded = EnemyStateData.Decode(wrapped, 1 + wrapped[0], true);
        Check(decoded.Id == 42 && decoded.Type == state.Type && decoded.X == 10.5f && decoded.Y == -3.25f
            && decoded.Life == 80 && decoded.Alive && !decoded.FacingRight && decoded.AnimationHash == -17001
            && decoded.AnimationTime == 1.75f, "snapshot preserves state, animation and UTF-8 prefix offsets");
        decoded = EnemyStateData.Decode(state.Encode(false), 0, false);
        Check(decoded.Type == "" && decoded.AnimationTime == 1.75f, "delta layout");
        byte[] body = state.Encode(true);
        for (int n = 0; n < body.Length; n++)
            Check(Rejected(() => EnemyStateData.Decode(body[..n], 0, true)), $"truncated state {n}");
        Check(Rejected(() => EnemyStateData.Decode(body.Concat(new byte[1]).ToArray(), 0, true)), "extra state bytes rejected");
        state.Life = float.NaN;
        Check(Rejected(() => EnemyStateData.Decode(state.Encode(true), 0, true)), "NaN health rejected");
        var hit = new EnemyHitData { Damage = 12.5f, Force = 2f, DamageType = 1, DamageElement = 4,
            SourceX = -2, SourceY = 5, Flags = EnemyHitData.ThrowbackByPosition | EnemyHitData.Unblockable };
        body = EnemyPackets.WriteDamage(7, "enemy|Type", 11, 22, hit);
        EnemyPackets.ReadDamage(body, 0, out int id, out string type, out float x, out float y, out var readHit);
        Check(id == 7 && type == "enemy|Type" && x == 11 && y == 22 && readHit.Damage == 12.5f
            && readHit.DamageType == 1 && readHit.DamageElement == 4 && readHit.Force == 2 && readHit.Flags == hit.Flags
            && readHit.SourceX == -2 && readHit.SourceY == 5 && !readHit.DirectDamage, "full combat hit survives transport");
        for (int n = 0; n < body.Length; n++)
            Check(Rejected(() => EnemyPackets.ReadDamage(body[..n], 0, out _, out _, out _, out _, out _)), $"truncated hit {n}");
        Check(Rejected(() => EnemyPackets.WriteDamage(1, new string('é', 128), 0, 0, hit)), "UTF-8 type length bound");
        hit.Damage = float.PositiveInfinity;
        Check(Rejected(() => EnemyPackets.WriteDamage(1, "x", 0, 0, hit)), "infinite hit rejected");
        hit.Damage = 8; hit.DirectDamage = true;
        EnemyPackets.ReadDamage(EnemyPackets.WriteDamage(1,"x",0,0,hit),0,out _,out _,out _,out _,out readHit);
        Check(readHit.DirectDamage, "scripted raw damage remains distinct from Hit replay");
        hit.DirectDamage = false;
        EnemyPackets.ReadPlayerHit(EnemyPackets.WritePlayerHit("Guest",hit),0,out string receiver,out readHit);
        Check(receiver == "Guest" && readHit.Flags == hit.Flags, "enemy player hit preserves combat rules");

        byte[] stream = Frame(NetworkType.EnemyDamage, body).Concat(Frame(NetworkType.Ping,new byte[] { 1 }))
            .Concat(Frame(NetworkType.EnemySyncRequest,Array.Empty<byte>())).ToArray();
        for (int split = 0; split <= stream.Length; split++)
        {
            var buffer = new PacketBuffer();
            var packets = new List<(NetworkType Type, byte[] Body)>();
            buffer.Append(stream[..split]);
            while (buffer.TryRead(out var t, out var b)) packets.Add((t,b));
            buffer.Append(stream[split..]);
            while (buffer.TryRead(out var t, out var b)) packets.Add((t,b));
            Check(packets.Count == 3 && packets[0].Type == NetworkType.EnemyDamage && packets[0].Body.SequenceEqual(body)
                && packets[1].Type == NetworkType.Ping && packets[2].Body.Length == 0, $"TCP split at {split}");
        }
        var bytewise = new PacketBuffer(); int count = 0;
        foreach (byte value in stream)
        {
            bytewise.Append(new[] { value });
            while (bytewise.TryRead(out _, out _)) count++;
        }
        Check(count == 3, "bytewise TCP fragmentation");
        var full = new PacketBuffer(); byte[] maximum = new byte[ushort.MaxValue];
        full.Append(Frame(NetworkType.Skin,maximum));
        Check(full.TryRead(out _,out var maxBody) && maxBody.Length == ushort.MaxValue, "maximum frame");
        full.Append(new byte[] { 10, 0, 1 }); full.Clear(); full.Append(Frame(NetworkType.Ping,new byte[] { 9 }));
        Check(full.TryRead(out _,out var fresh) && fresh.SequenceEqual(new byte[] { 9 }), "disconnect clears partial frames");
        Check(EnemyAuthority.Resolve(new[] { "Zulu","Alpha" }, "absent") == "Alpha"
            && EnemyAuthority.Resolve(new[] { "Alpha","Zulu" }, "absent") == "Alpha", "fallback authority ignores arrival order");
        Check(EnemyAuthority.Resolve(new[] { "Alpha","Zulu" }, "Zulu") == "Zulu", "session host has priority");
        NetworkIntegration();
        Console.WriteLine($"EnemyProtocolSmoke: PASS ({checks} checks; protocol {Protocol.VERSION}; real server relay)");
    }
}
