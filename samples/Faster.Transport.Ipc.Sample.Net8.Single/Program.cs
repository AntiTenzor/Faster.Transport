using System;
using System.Text;
using System.Threading;

using Faster.Transport.Ipc;
using Faster.Transport.Contracts;



namespace Faster.Transport.Ipc.Sample.Single;

class Program
{
    static void Main()
    {
        const string Base = "FasterIpcDemo";
        var server = new MappedReactor(Base);
        server.OnConnected += SeverOnConnected;
        server.OnReceived += ServerOnReceived;
        server.Start();

        var c1 = new MappedParticle(Base, 0xA1UL);
        var c2 = new MappedParticle(Base, 0xB2UL);

        c1.OnReceived += (particale, mem) => Console.WriteLine($"[C1] <- {Encoding.UTF8.GetString(mem.Span)}");
        c2.OnReceived += (particale, mem) => Console.WriteLine($"[C2] <- {Encoding.UTF8.GetString(mem.Span)}");

        c1.Start();
        c2.Start();

        Console.WriteLine("Type '1 hi' or '2 hi' to send FROM client1/client2. Type /exit to quit.");
        while (true)
        {
            string line = Console.ReadLine() ?? "";

            if ("" == line)
                continue;

            if ("/exit" == line)
                break;

            if ((line.Length > 2) &&
                (line[0] == '1' || line[0] == '2') && (line[1] == ' '))
            {
                string text = line[2..];
                byte[] bytes = Encoding.UTF8.GetBytes(text);

                if (line[0] == '1')
                    c1.Send(bytes);
                else
                    c2.Send(bytes);
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("[SERVER] Unknown target, so I publish the same message to all clients.");

                byte[] bytes = Encoding.UTF8.GetBytes(line);

                server.Broadcast(bytes);
            }
        }

        c1.Dispose();
        c2.Dispose();

        server.Dispose();
    }

    private static void ServerOnReceived(IParticle particle, ReadOnlyMemory<byte> memory)
    {
        if (particle == null)
        {
            Console.WriteLine("[ServerOnReceived] Argument 'particle' is NULL. It is a mistake.");
            return;
        }

        try
        {
            string msg = Encoding.UTF8.GetString(memory.Span);
            // Console.WriteLine($"[ServerOnReceived] <- {particle}: {msg}");

            string respStr = "echo: " + msg;
            byte[] response = Encoding.UTF8.GetBytes(respStr);

            particle.Send(response);
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine("[ServerOnReceived] Error: " + ex.Message + Environment.NewLine + Environment.NewLine +
                ex + Environment.NewLine);
            Console.WriteLine();
        }
    }

    private static void SeverOnConnected(IParticle client)
    {
        if (client == null)
        {
            Console.WriteLine("[OnConnectedToSever] Argument 'client' is NULL. It is a mistake.");
            return;
        }

        try
        {
            string ids = client.ToString() ?? "NULL";
            Console.WriteLine("[SERVER] Client " + ids + " connected");
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine("[OnConnectedToSever] Error: " + ex.Message + Environment.NewLine + Environment.NewLine +
                ex + Environment.NewLine);
            Console.WriteLine();
        }
    }
}
