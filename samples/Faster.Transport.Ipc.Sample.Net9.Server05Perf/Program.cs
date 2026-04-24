using System;
using System.Text;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;

using Faster.Transport;
using Faster.Transport.Contracts;



namespace Faster.Transport.Ipc.Sample.Net9.Server05Perf;

public class Program
{
    // Configuration
    public const string IpcChannelName = "IpcServer05Perf";

    public const int totalMessages = 10_000_000;
    public const int batchSize = 1_000; // Send in batches to reduce overhead
    public const int warmupMessages = 100_000; // Warmup to stabilize performance

    //private static readonly DateTimeProvider timeProv = new DateTimeProvider();

    static void Main(string[] args)
    {
        Console.WriteLine();
        Console.WriteLine("Starting ENHANCED IPC PUBLISHER (SYNC) with detailed metrics...");

        // Ensure high priority for this thread
        System.Threading.Thread.CurrentThread.Priority = System.Threading.ThreadPriority.Highest;
        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;

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
        Console.WriteLine();
        Console.WriteLine($"Warming up with {warmupMessages:N0} messages...");
        PublishWithTimestamps(client, warmupMessages, batchSize, isWarmup: true);

        // Clear timestamps for actual measurement
        //sendTimestamps.Clear();
        GC.Collect();
        GC.WaitForPendingFinalizers();

        // Actual measurement phase
        Console.WriteLine($"Starting actual measurement for {totalMessages:N0} messages...");
        var stopwatch = Stopwatch.StartNew();

        long messagesSent = PublishWithTimestamps(client, totalMessages, batchSize, isWarmup: false);

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

    static long PublishWithTimestamps(IParticle client, int messageCount, int batchSize, bool isWarmup)
    {
        // Pre-allocate buffer to avoid repeated allocations
        byte[] buffer = new byte[1024]; // Adjust size based on your message format

        long sentCount = 0;
        var stopwatch = new Stopwatch();

        if (!isWarmup)
        {
            stopwatch.Start();
        }

        for (int i = 0; i < messageCount; i += batchSize)
        {
            int currentBatchSize = Math.Min(batchSize, messageCount - i);
            
            for (int j = 0; j < currentBatchSize; j++)
            {
                int messageNumber = i + j;

                // Avoid formatted strings in 'HighPerf' code

                // Build message without string allocation where possible
                int messageLength = BuildMessageBytes(buffer, messageNumber);

                // Send synchronously (blocking)
                client.Send(buffer.AsSpan(0, messageLength));
            }

            sentCount += currentBatchSize;

            // Progress reporting every 1 million messages (only for actual measurement)
            if (!isWarmup && (sentCount % 1_000_000 == 0))
            {
                double elapsedSec = stopwatch.Elapsed.TotalSeconds;
                double currentRate = sentCount / elapsedSec;
                Console.WriteLine($"  Progress: {sentCount:N0} / {messageCount:N0} messages " +
                                  $"({currentRate:N0} msg/s, {elapsedSec:F2}s elapsed)");
            }
        } // End for (int i = 0; i < messageCount; i += batchSize)

        if (!isWarmup)
        {
            stopwatch.Stop();
        }

        return sentCount;
    }

    static int BuildMessageBytes(byte[] buffer, int messageNumber)
    {
        // Fast manual formatting: "MSG_ID|TIMESTAMP"
        // Example: "12345678|1234567890123"
        string messageId = messageNumber.ToString(CultureInfo.InvariantCulture);
        //string timestamp = DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
        //string ticks = timeProv.UtcNowTicks.ToString(CultureInfo.InvariantCulture);
        string ticks = Stopwatch.GetTimestamp().ToString(CultureInfo.InvariantCulture);

        int pos = 0;

        // Write message ID
        for (int i = 0; i < messageId.Length; i++)
        {
            buffer[pos++] = (byte)messageId[i];
        }

        // Write separator
        buffer[pos++] = (byte)'|';

        // Write timestamp
        for (int i = 0; i < ticks.Length; i++)
        {
            buffer[pos++] = (byte)ticks[i];
        }

        return pos; // Return actual length used
    }
}
