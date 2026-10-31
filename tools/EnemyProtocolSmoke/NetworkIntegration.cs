using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Blasphemous.Multiplayer.Common;
using Blasphemous.Multiplayer.Common.Combat;
using Blasphemous.Multiplayer.Common.Enums;
using Blasphemous.Multiplayer.Server;

static partial class Program
{
    sealed class WirePeer : IDisposable
    {
        readonly TcpClient client = new();
        readonly PacketBuffer buffer = new();
        public WirePeer(int port, string name)
        {
            client.Connect(IPAddress.Loopback,port); client.NoDelay = true;
            byte[] text = Encoding.UTF8.GetBytes(name);
            Send(NetworkType.Intro,new byte[] { Protocol.VERSION, 0, (byte)text.Length }.Concat(text).Concat(new byte[] { 0,1 }).ToArray());
            Check(Wait(NetworkType.Intro)[0] == (byte)RefusalType.Accepted, $"{name} handshake");
        }
        public void Send(NetworkType type, byte[] body) => client.GetStream().Write(Frame(type,body));
        public void Fragment(NetworkType type, byte[] body)
        {
            byte[] frame = Frame(type,body);
            client.GetStream().Write(frame,0,1); Thread.Sleep(20);
            client.GetStream().Write(frame,1,2); Thread.Sleep(20);
            client.GetStream().Write(frame,3,frame.Length-3);
        }
        public byte[] Wait(NetworkType wanted, int timeout = 2500, bool optional = false)
        {
            var until = DateTime.UtcNow.AddMilliseconds(timeout);
            do
            {
                while (buffer.TryRead(out var type,out var body))
                    if (type == wanted) return body;
                if (client.Available > 0)
                {
                    byte[] bytes = new byte[client.Available];
                    int size = client.GetStream().Read(bytes,0,bytes.Length); buffer.Append(bytes[..size]);
                }
                else Thread.Sleep(5);
            } while (DateTime.UtcNow < until);
            if (optional) return null!;
            throw new Exception($"Timed out waiting for {wanted}");
        }
        public void Dispose() => client.Dispose();
    }
    static void NetworkIntegration()
    {
        var probe = new TcpListener(IPAddress.Loopback,0); probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        var server = new Server(4,null);
        Check(server.Start(port), "real server starts");
        try
        {
            using var host = new WirePeer(port,"Host");
            host.Send(NetworkType.EnterScene,Encoding.UTF8.GetBytes("roomA"));
            using var guest = new WirePeer(port,"Guest");
            guest.Send(NetworkType.EnterScene,Encoding.UTF8.GetBytes("roomA"));
            using var observer = new WirePeer(port,"Observer");
            observer.Send(NetworkType.EnterScene,Encoding.UTF8.GetBytes("roomB"));
            // A following packet on each connection is a barrier for its EnterScene packet.
            guest.Send(NetworkType.Ping,new byte[6]); guest.Wait(NetworkType.Ping);
            observer.Send(NetworkType.Ping,new byte[6]); observer.Wait(NetworkType.Ping);
            var state = new EnemyStateData { Id = 123, Type = "enemy", Life = 50, Alive = true };
            byte[] snapshot = state.Encode(true);
            host.Fragment(NetworkType.EnemySnapshot,snapshot);
            byte[] received = guest.Wait(NetworkType.EnemySnapshot);
            Check(Encoding.UTF8.GetString(received,1,received[0]) == "Host"
                && EnemyStateData.Decode(received,1+received[0],true).Id == 123, "fragmented snapshot relayed with authenticated sender");
            Check(observer.Wait(NetworkType.EnemySnapshot,100,true) == null, "combat does not leak into other rooms");
            guest.Send(NetworkType.EnemySnapshot,snapshot);
            Check(host.Wait(NetworkType.EnemySnapshot,100,true) == null, "server rejects guest authority packets");
            var hit = new EnemyHitData { Damage = 10, Force = 2, DamageElement = 3 };
            guest.Fragment(NetworkType.EnemyDamage,EnemyPackets.WriteDamage(123,"enemy",0,0,hit));
            received = host.Wait(NetworkType.EnemyDamage);
            EnemyPackets.ReadDamage(received,1+received[0],out int id,out _,out _,out _,out var data);
            Check(id == 123 && data.Damage == 10 && data.DamageElement == 3, "fragmented guest damage reaches host");
            Check(observer.Wait(NetworkType.EnemyDamage,100,true) == null, "damage routes only to room host");
            guest.Send(NetworkType.EnemyPlayerHit,EnemyPackets.WritePlayerHit("Host",hit));
            Check(host.Wait(NetworkType.EnemyPlayerHit,100,true) == null, "guest cannot forge enemy player hits");
            host.Send(NetworkType.LeaveScene,new byte[] { 0 });
            host.Send(NetworkType.Ping,new byte[6]); host.Wait(NetworkType.Ping);
            observer.Send(NetworkType.EnterScene,Encoding.UTF8.GetBytes("roomA"));
            observer.Send(NetworkType.Ping,new byte[6]); observer.Wait(NetworkType.Ping);
            guest.Send(NetworkType.EnemySnapshot,snapshot);
            received = observer.Wait(NetworkType.EnemySnapshot);
            Check(Encoding.UTF8.GetString(received,1,received[0]) == "Guest", "fallback host takes over after session host leaves room");
            host.Send(NetworkType.EnterScene,Encoding.UTF8.GetBytes("roomA"));
            host.Send(NetworkType.Ping,new byte[6]); host.Wait(NetworkType.Ping);
            guest.Send(NetworkType.EnemySnapshot,snapshot);
            Check(observer.Wait(NetworkType.EnemySnapshot,100,true) == null, "returning session host reclaims authority");
        }
        finally
        {
            // Server owns its transport privately; stop it without adding a test-only public API.
            var transport = typeof(Server).GetField("server",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(server)!;
            transport.GetType().GetMethod("Stop")!.Invoke(transport,null);
        }
    }
}
