using System;
using System.Text;

using Faster.Transport;
using Faster.Transport.Contracts;



namespace Faster.Transport.Ipc.Sample.Net8.Multi;

internal class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine();
        Console.WriteLine("Starting sample MILTI (whatever this means)...");

        //var server = new ParticleBuilder()
        //    .UseMode(TransportMode.Ipc)
        //    .WithChannel("FasterIpcDemo", isServer: true)
        //    .OnConnected(p => Console.WriteLine("Client connected"))
        //    .OnReceived((p, data) =>
        //    {
        //        Console.WriteLine($"[Server] Received: {Encoding.UTF8.GetString(data.Span)}");
        //        p.Send(Encoding.UTF8.GetBytes("Echo: " + Encoding.UTF8.GetString(data.Span)));
        //    })
        //    .Build();

        // Let's do step-by-step:
        ReactorBuilder builder = new ReactorBuilder();
        builder = builder.UseMode(TransportMode.Ipc); // Use Shared Memory
        builder = builder.WithChannel("FasterIpcDemo");
        //builder = builder.WithGlobal(false);
        builder = builder.OnConnected(p => Console.WriteLine("Client connected"));
        builder = builder.OnReceived((p, data) =>
            {
                Console.WriteLine($"[Server] Received: {Encoding.UTF8.GetString(data.Span)}");
                p.Send(Encoding.UTF8.GetBytes("Echo: " + Encoding.UTF8.GetString(data.Span)));
            });
        IReactor server = builder.Build();
        Console.WriteLine("   This server is of type     : {0}", server.GetType().FullName);
        Console.WriteLine("   Base name of the channel is: {0}", "FasterIpcDemo");
        Console.WriteLine();

        server.Start();
        Console.WriteLine($"Publisher started. Waiting incoming connection...");


        var client = new ParticleBuilder()
            .UseMode(TransportMode.Ipc)
            .WithChannel("FasterIpcDemo")
            .OnReceived((p, data) =>
                Console.WriteLine($"[Client] Got reply: {Encoding.UTF8.GetString(data.Span)}"))
            .Build();

        client.Send(Encoding.UTF8.GetBytes("hello world"));

        Console.ReadLine();
    }
}
