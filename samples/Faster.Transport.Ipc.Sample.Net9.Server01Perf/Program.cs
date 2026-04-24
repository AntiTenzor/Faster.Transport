using System;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;

using Faster.Transport;
using Faster.Transport.Contracts;



namespace Faster.Transport.Ipc.Sample.Net9.Server01Perf;

public class Program
{
    // Configuration
    public const string IpcChannelName = "IpcServer01Perf";

    public const int totalMessages = 10_000_000;
    public const int batchSize = 1_000; // Send in batches to reduce overhead
    public const int warmupMessages = 100_000; // Warmup to stabilize performance

    static void Main(string[] args)
    {
        Console.WriteLine();
        Console.WriteLine("Starting IPC PUBLISHER with performance measurement...");

        //var publisher = new ParticleBuilder()
        //    .UseMode(TransportMode.Ipc) // Use Shared Memory
        //    .WithChannel(Base)
        //    .WithAutoReconnect()
        //    .OnConnected(SeverOnConnected)
        //    .Build();

        // Let's do step-by-step:
        // DeepSeek made an error here. ParticleBuilder is used to build CLIENT, not the server.
        //ParticleBuilder builder = new ParticleBuilder();

        ReactorBuilder builder = new ReactorBuilder();
        builder = builder.UseMode(TransportMode.Ipc); // Use Shared Memory
        builder = builder.WithChannel(IpcChannelName);
        builder = builder.WithGlobal(false);
        builder = builder.OnConnected(SeverOnConnected);
        IReactor server = builder.Build();
        Console.WriteLine("   This server is of type     : {0}", server.GetType().FullName);
        Console.WriteLine("   Base name of the channel is: {0}", IpcChannelName);
        Console.WriteLine();

        server.Start();
        Console.WriteLine($"Publisher started. Target: {totalMessages:N0} messages. Waiting incoming connection...");

        // Keep running to allow subscribers to finish
        Console.WriteLine("Publisher continuing. RUN IPC CLIENT NOW. Then press Enter to exit.");
        Console.ReadLine();

        server.OnConnected -= SeverOnConnected;

        server.Dispose();

        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine();
    }

    private static void SeverOnConnected(IParticle client)
    {
        // Warmup phase
        Console.WriteLine($"Warming up with {warmupMessages:N0} messages...");
        PublishMessages(client, warmupMessages, batchSize, isWarmup: true);

        // Actual measurement phase
        Console.WriteLine($"Starting actual measurement for {totalMessages:N0} messages...");
        var stopwatch = Stopwatch.StartNew();

        long messagesSent = PublishMessages(client, totalMessages, batchSize, isWarmup: false);

        stopwatch.Stop();

        // Calculate statistics
        double totalSeconds = stopwatch.Elapsed.TotalSeconds;
        double messagesPerSecond = messagesSent / totalSeconds;
        double microsecondsPerMessage = (stopwatch.Elapsed.TotalMilliseconds * 1000) / messagesSent;

        Console.WriteLine("\n========== PUBLISHER PERFORMANCE RESULTS ==========");
        Console.WriteLine($"Total Messages Sent:     {messagesSent:N0}");
        Console.WriteLine($"Total Time:              {stopwatch.Elapsed.TotalSeconds:F3} seconds");
        Console.WriteLine($"Messages per Second:     {messagesPerSecond:N0} msg/s");
        Console.WriteLine($"Microseconds per Message: {microsecondsPerMessage:F2} μs");
        Console.WriteLine($"Total Elapsed:           {stopwatch.Elapsed}");
        Console.WriteLine("===================================================\n");
    }

    static long PublishMessages(IParticle client, int messageCount, int batchSize, bool isWarmup)
    {
        long sentCount = 0;
        var stopwatch = new Stopwatch();

        if (!isWarmup)
        {
            stopwatch.Start();
        }

        for (int i = 0; i < messageCount; i += batchSize)
        {
            int currentBatchSize = Math.Min(batchSize, messageCount - i);
            var tasks = new List<System.Threading.Tasks.Task>(currentBatchSize);

            for (int j = 0; j < currentBatchSize; j++)
            {
                int messageNumber = i + j;
                // Avoid formatted strings in 'HighPerf' code
                //string messageContent = $"Msg_{messageNumber}_{DateTime.UtcNow.Ticks}";
                string messageContent = "Msg_" + messageNumber + "_" + DateTime.UtcNow.Ticks;
                byte[] data = Encoding.UTF8.GetBytes(messageContent);

                // For ultra-high performance, consider reusing buffers
                // but keeping it simple for this example
                tasks.Add(client.SendAsync(data).AsTask());

                //tasks.Add(Task.Run(() => client.Send(data)));
            }

            //System.Threading.Tasks.Task.WhenAll(tasks);
            var task = System.Threading.Tasks.Task.WhenAll(tasks);
            task.Wait();
            sentCount += currentBatchSize;

            // Progress reporting every 1 million messages (only for actual measurement)
            if (!isWarmup && sentCount % 1_000_000 == 0)
            {
                double elapsedSec = stopwatch.Elapsed.TotalSeconds;
                double currentRate = sentCount / elapsedSec;
                Console.WriteLine($"  Progress: {sentCount:N0} / {messageCount:N0} messages " +
                                  $"({currentRate:F0} msg/s, {elapsedSec:F2}s elapsed)");
            }
        }

        if (!isWarmup)
        {
            stopwatch.Stop();
        }

        return sentCount;
    }
}
