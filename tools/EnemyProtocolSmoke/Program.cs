using System.Text;
using Blasphemous.Multiplayer.Common.Enums;

/// <summary>
/// Offline smoke test for room-enemy packet layout (protocol v4).
/// Mirrors client BuildEnemyBody / server name-prefix / client ReadEnemyBody.
/// </summary>
static class Program
{
    const byte Alive = 1 << 0;
    const byte FacingRight = 1 << 1;

    static int Main()
    {
        int failures = 0;
        failures += Check((byte)NetworkType.EnemySnapshot == 13, "EnemySnapshot=13");
        failures += Check((byte)NetworkType.EnemyState == 14, "EnemyState=14");
        failures += Check((byte)NetworkType.EnemyDamage == 15, "EnemyDamage=15");
        failures += Check((byte)NetworkType.EnemyDeath == 16, "EnemyDeath=16");
        failures += Check((byte)NetworkType.EnemySyncRequest == 17, "EnemySyncRequest=17");

        // Snapshot body
        byte[] body = BuildEnemyBody(42, "EN01", 10.5f, -3.25f, 80f, alive: true, facingRight: false, includeTypeId: true);
        byte[] wrapped = WrapName("HostPlayer", body);
        int idx = UnwrapName(wrapped, out string name);
        failures += Check(name == "HostPlayer", "name prefix");
        ReadEnemyBody(wrapped, idx, includeTypeId: true,
            out int netId, out string typeId, out float x, out float y, out float life, out bool alive, out bool facingRight);
        failures += Check(netId == 42, "netId");
        failures += Check(typeId == "EN01", "typeId");
        failures += Check(Near(x, 10.5f) && Near(y, -3.25f), "position");
        failures += Check(Near(life, 80f), "life");
        failures += Check(alive && !facingRight, "flags");

        // Damage body
        var dmg = new List<byte>();
        dmg.AddRange(BitConverter.GetBytes(7));
        dmg.AddRange(BitConverter.GetBytes(12.5f));
        byte[] dmgWrapped = WrapName("Attacker", dmg.ToArray());
        int dIdx = UnwrapName(dmgWrapped, out string attacker);
        int dNet = BitConverter.ToInt32(dmgWrapped, dIdx);
        float dAmt = BitConverter.ToSingle(dmgWrapped, dIdx + 4);
        failures += Check(attacker == "Attacker" && dNet == 7 && Near(dAmt, 12.5f), "damage packet");

        // Death body
        byte[] deathWrapped = WrapName("HostPlayer", BitConverter.GetBytes(99));
        int deathIdx = UnwrapName(deathWrapped, out _);
        failures += Check(BitConverter.ToInt32(deathWrapped, deathIdx) == 99, "death packet");

        if (failures == 0)
        {
            Console.WriteLine("EnemyProtocolSmoke: PASS (protocol v4 enemy packets)");
            return 0;
        }

        Console.WriteLine($"EnemyProtocolSmoke: FAIL ({failures})");
        return 1;
    }

    static int Check(bool ok, string label)
    {
        Console.WriteLine(ok ? $"  OK  {label}" : $"  FAIL {label}");
        return ok ? 0 : 1;
    }

    static bool Near(float a, float b) => Math.Abs(a - b) < 0.0001f;

    static byte[] BuildEnemyBody(int netId, string enemyTypeId, float x, float y, float life, bool alive, bool facingRight, bool includeTypeId)
    {
        byte flags = 0;
        if (alive) flags |= Alive;
        if (facingRight) flags |= FacingRight;

        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes(netId));
        bytes.Add(flags);
        bytes.AddRange(BitConverter.GetBytes(x));
        bytes.AddRange(BitConverter.GetBytes(y));
        bytes.AddRange(BitConverter.GetBytes(life));
        if (includeTypeId)
        {
            byte[] idBytes = Encoding.UTF8.GetBytes(enemyTypeId);
            bytes.Add((byte)idBytes.Length);
            bytes.AddRange(idBytes);
        }
        return bytes.ToArray();
    }

    static byte[] WrapName(string name, byte[] body)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        var bytes = new List<byte> { (byte)nameBytes.Length };
        bytes.AddRange(nameBytes);
        bytes.AddRange(body);
        return bytes.ToArray();
    }

    static int UnwrapName(byte[] data, out string name)
    {
        byte len = data[0];
        name = Encoding.UTF8.GetString(data, 1, len);
        return 1 + len;
    }

    static void ReadEnemyBody(byte[] data, int idx, bool includeTypeId,
        out int netId, out string enemyTypeId, out float x, out float y, out float life, out bool alive, out bool facingRight)
    {
        netId = BitConverter.ToInt32(data, idx);
        byte flags = data[idx + 4];
        alive = (flags & Alive) != 0;
        facingRight = (flags & FacingRight) != 0;
        x = BitConverter.ToSingle(data, idx + 5);
        y = BitConverter.ToSingle(data, idx + 9);
        life = BitConverter.ToSingle(data, idx + 13);
        enemyTypeId = string.Empty;
        if (includeTypeId)
        {
            byte typeLen = data[idx + 17];
            enemyTypeId = Encoding.UTF8.GetString(data, idx + 18, typeLen);
        }
    }
}
