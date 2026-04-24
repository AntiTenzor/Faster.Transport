using System;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;

using Faster.Transport;
using Faster.Transport.Contracts;



namespace Faster.Transport.Ipc.Sample.Net9.Server03Perf;

public class Program
{
    // Configuration
    public const string IpcChannelName = "IpcServer01Perf";
    public const string InprocChannelName = "InprocServer03Perf";

    public const int totalMessages = 10_000_000;
    public const int batchSize = 1_000; // Send in batches to reduce overhead
    public const int warmupMessages = 100_000; // Warmup to stabilize performance

    private static readonly object _ipcLockObj = new object();
    private static readonly Stopwatch _ipcStopwatch = new Stopwatch();

    private static long _inprocReceivedCount = 0;
    private static int _inprocLastProgressPercent = 0;
    private static readonly object _inprocLockObj = new object();
    private static readonly Stopwatch _inprocStopwatch = new Stopwatch();

    static void Main(string[] args)
    {
        RunInprocPerfMain();

        RunIpcPerfMain();
    }

    static void RunInprocPerfMain()
    {
        Console.WriteLine();
        Console.WriteLine("Starting INPROC PUBLISHER with performance measurement...");

        // Let's do step-by-step:
        IReactor server = null;
        {
            ReactorBuilder builder = new ReactorBuilder();
            builder = builder.UseMode(TransportMode.Inproc); // Use Shared Memory
            builder = builder.WithChannel(InprocChannelName);
            //builder = builder.WithGlobal(false);
            builder = builder.OnConnected(SeverOnConnected);
            server = builder.Build();
        }
        Console.WriteLine("   This server is of type     : {0}", server.GetType().FullName);
        Console.WriteLine("   Base name of the channel is: {0}", InprocChannelName);
        Console.WriteLine();

        server.Start();
        Console.WriteLine($"Publisher started. Target: {totalMessages:N0} messages. Waiting incoming connection...");

        // Keep running to allow subscribers to finish
        Console.WriteLine("Publisher continuing. RUN INPROC CLIENT NOW. Then press Enter to exit.");

        Thread.Sleep(5_000);

        {
            // Monitor completion
            var monitorThread = new Thread(() => MonitorInprocCompletion(_inprocStopwatch, totalMessages));
            monitorThread.Start();

            // Let's do step-by-step:
            IParticle client = null;
            {
                ParticleBuilder builder = new ParticleBuilder();
                builder = builder.UseMode(TransportMode.Inproc); // Must match the publisher
                builder = builder.WithChannel(InprocChannelName);
                //builder = builder.WithGlobal(false);
                builder = builder.OnReceived(SubscriberOnReceived);
                client = builder.Build();
            }
            Console.WriteLine("   This client is of type     : {0}", client.GetType().FullName);
            Console.WriteLine("   Base name of the channel is: {0}", InprocChannelName);
            Console.WriteLine();

            Console.WriteLine($"Subscriber started. Expecting {totalMessages:N0} messages...\n");



            // Keep the subscriber running
            Console.WriteLine("\nSubscriber running. Press Enter to exit and see final results.");
            Console.ReadLine();

            // Ensure stopwatch is stopped
            if (_inprocStopwatch.IsRunning)
            {
                _inprocStopwatch.Stop();
            }

            PrintFinalResults(_inprocStopwatch, totalMessages + warmupMessages, _inprocReceivedCount);

            client.Dispose();

        }

        Console.ReadLine();

        server.OnConnected -= SeverOnConnected;

        server.Dispose();

        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine();
    }

    static void RunIpcPerfMain()
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
            //var tasks = new List<System.Threading.Tasks.Task>(currentBatchSize);

            for (int j = 0; j < currentBatchSize; j++)
            {
                int messageNumber = i + j;
                // Avoid formatted strings in 'HighPerf' code
                //string messageContent = $"Msg_{messageNumber}_{DateTime.UtcNow.Ticks}";
                string messageContent = "Msg_" + messageNumber + "_" + DateTime.UtcNow.Ticks;
                byte[] data = Encoding.UTF8.GetBytes(messageContent);

                // For ultra-high performance, consider reusing buffers
                // but keeping it simple for this example
                //tasks.Add(client.SendAsync(data).AsTask());

                //tasks.Add(Task.Run(() => client.Send(data)));

                client.Send(data);
            }

            //System.Threading.Tasks.Task.WhenAll(tasks);
            //var task = System.Threading.Tasks.Task.WhenAll(tasks);
            //task.Wait();
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

    private static void SubscriberOnReceived(IParticle particle, ReadOnlyMemory<byte> memory)
    {
        // Increment counter atomically
        long currentCount = Interlocked.Increment(ref _inprocReceivedCount);

        // Start timing on first message
        if (currentCount == warmupMessages)
        {
            lock (_inprocLockObj)
            {
                if (!_inprocStopwatch.IsRunning)
                {
                    _inprocStopwatch.Start();
                }
            }
        }

        // Progress reporting every 1 million messages
        if (currentCount % 1_000_000 == 0)
        {
            lock (_inprocLockObj)
            {
                double elapsedSec = _inprocStopwatch.Elapsed.TotalSeconds;
                double currentRate = currentCount / elapsedSec;
                int percentComplete = (int)((double)currentCount / totalMessages * 100);

                if (percentComplete != _inprocLastProgressPercent)
                {
                    _inprocLastProgressPercent = percentComplete;
                    Console.WriteLine($"  Progress: {currentCount:N0} / {totalMessages:N0} messages " +
                                      $"({percentComplete}%) - Rate: {currentRate:F0} msg/s, " +
                                      $"Elapsed: {elapsedSec:F2}s");
                }

                //string text = Encoding.UTF8.GetString(memory.Span);
                //var messageNum = int.Parse(text.Split('_')[1]);
                //Console.WriteLine("     Last msg: " + text);
            }
        }

        // Optional: Verify message content (comment out for max performance)
        // string text = Encoding.UTF8.GetString(msg.Span);
        // var messageNum = int.Parse(text.Split('_')[1]);
    }

    static void MonitorInprocCompletion(Stopwatch stopwatch, long expectedMessages)
    {
        while (_inprocReceivedCount < expectedMessages)
        {
            Thread.Sleep(100); // Check every 100ms
        }

        // All messages received
        if (stopwatch.IsRunning)
        {
            stopwatch.Stop();
        }

        Console.WriteLine("\n\n*** ALL INPROC MESSAGES RECEIVED! ***\n");
        PrintFinalResults(stopwatch, expectedMessages + warmupMessages, _inprocReceivedCount);

        // Force exit after 2 seconds to allow user to see results
        Thread.Sleep(7_000);
        //Environment.Exit(0);
    }

    static void PrintFinalResults(Stopwatch stopwatch, long expectedReceived, long actualReceived)
    {
        Console.WriteLine("\n========== SUBSCRIBER PERFORMANCE RESULTS ==========");
        Console.WriteLine($"Expected Messages:       {expectedReceived:N0}");
        Console.WriteLine($"Messages Received:       {actualReceived:N0}");
        Console.WriteLine($"Message Loss:            {expectedReceived - actualReceived:N0} " +
                          $"({(1 - (double)actualReceived / expectedReceived) * 100:F2}%)");

        if ((actualReceived > 0) &&
            (stopwatch.Elapsed.TotalSeconds > 0))
        {
            double totalSeconds = stopwatch.Elapsed.TotalSeconds;
            double messagesPerSecond = actualReceived / totalSeconds;
            double microsecondsPerMessage = (stopwatch.Elapsed.TotalMilliseconds * 1000) / actualReceived;

            Console.WriteLine($"\nTotal Time:              {totalSeconds:F3} seconds");
            Console.WriteLine($"Messages per Second:     {messagesPerSecond:N0} msg/s");
            Console.WriteLine($"Microseconds per Message: {microsecondsPerMessage:F2} μs");
            Console.WriteLine($"Total Elapsed:           {stopwatch.Elapsed}");

            // Performance assessment
            Console.WriteLine("\n--- Performance Assessment ---");
            if (messagesPerSecond >= 10_000_000)
            {
                Console.WriteLine("✅ EXCELLENT! Target achieved: 10M+ messages/second!");
            }
            else if (messagesPerSecond >= 5_000_000)
            {
                Console.WriteLine($"⚠️ GOOD: {messagesPerSecond:F0} msg/s - Close to 10M target");
            }
            else if (messagesPerSecond >= 1_000_000)
            {
                Console.WriteLine($"⚠️ MODERATE: {messagesPerSecond:F0} msg/s - Below 10M target");
            }
            else
            {
                Console.WriteLine($"❌ SLOW: {messagesPerSecond:F0} msg/s - Far below 10M target");
            }
        }
        else
        {
            Console.WriteLine("No messages received or invalid timing data.");
        }
        Console.WriteLine("====================================================\n");
    }
}
