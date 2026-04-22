using System;
using System.Text;
using System.Threading;
using System.Diagnostics;
//using System.Threading.Tasks;
using System.Collections.Generic;

using Faster.Transport;
using Faster.Transport.Contracts;



namespace Faster.Transport.Ipc.Sample.Net9.Client01Perf;

internal class Program
{
    // Configuration
    public const string Base = Faster.Transport.Ipc.Sample.Net9.Server01Perf.Program.Base; // "FasterIpcServer01Perf";

    public const int expectedMessages = Faster.Transport.Ipc.Sample.Net9.Server01Perf.Program.totalMessages; // 10_000_000;
    public const int warmupMessages = Faster.Transport.Ipc.Sample.Net9.Server01Perf.Program.warmupMessages; // 100_000; // Warmup to stabilize performance

    private static long _receivedCount = 0;
    private static long _lastReportedCount = 0;
    private static readonly Stopwatch _stopwatch = new Stopwatch();
    private static readonly object _lockObj = new object();
    private static int _lastProgressPercent = 0;

    static void Main(string[] args)
    {
        Console.WriteLine("Starting SUBSCRIBER with performance measurement...");

        // Monitor completion
        var monitorThread = new Thread(() => MonitorCompletion(expectedMessages));
        monitorThread.Start();

        // Let's do step-by-step:
        // DeepSeek made an error here. ReactorBuilder is used to build SERVER, not the client.
        // var subscriber = new ReactorBuilder();

        ParticleBuilder builder = new ParticleBuilder();
        builder = builder.UseMode(TransportMode.Ipc); // Must match the publisher
        builder = builder.WithChannel(Base);
        builder = builder.WithGlobal(false);
        builder = builder.OnReceived(SubscriberOnReceived);
        IParticle client = builder.Build();
        Console.WriteLine("   This client is of type     : {0}", client.GetType().FullName);
        Console.WriteLine("   Base name of the channel is: {0}", Base);
        Console.WriteLine();

        Console.WriteLine($"Subscriber started. Expecting {expectedMessages:N0} messages...\n");



        // Keep the subscriber running
        Console.WriteLine("\nSubscriber running. Press Enter to exit and see final results.");
        Console.ReadLine();

        // Ensure stopwatch is stopped
        if (_stopwatch.IsRunning)
        {
            _stopwatch.Stop();
        }

        PrintFinalResults(expectedMessages, _receivedCount);

        client.Dispose();
    }

    private static void SubscriberOnReceived(IParticle particle, ReadOnlyMemory<byte> memory)
    {
        // Increment counter atomically
        long currentCount = Interlocked.Increment(ref _receivedCount);

        // Start timing on first message
        if (currentCount == warmupMessages)
        {
            lock (_lockObj)
            {
                if (!_stopwatch.IsRunning)
                {
                    _stopwatch.Start();
                }
            }
        }

        // Progress reporting every 1 million messages
        if (currentCount % 1_000_000 == 0)
        {
            lock (_lockObj)
            {
                double elapsedSec = _stopwatch.Elapsed.TotalSeconds;
                double currentRate = currentCount / elapsedSec;
                int percentComplete = (int)((double)currentCount / expectedMessages * 100);

                if (percentComplete != _lastProgressPercent)
                {
                    _lastProgressPercent = percentComplete;
                    Console.WriteLine($"  Progress: {currentCount:N0} / {expectedMessages:N0} messages " +
                                      $"({percentComplete}%) - Rate: {currentRate:F0} msg/s, " +
                                      $"Elapsed: {elapsedSec:F2}s");
                }

                string text = Encoding.UTF8.GetString(memory.Span);
                //var messageNum = int.Parse(text.Split('_')[1]);
                Console.WriteLine("     Last msg: " + text);
            }
        }

        // Optional: Verify message content (comment out for max performance)
        // string text = Encoding.UTF8.GetString(msg.Span);
        // var messageNum = int.Parse(text.Split('_')[1]);
    }

    static void MonitorCompletion(int expectedMessages)
    {
        while (_receivedCount < expectedMessages)
        {
            Thread.Sleep(100); // Check every 100ms
        }

        // All messages received
        if (_stopwatch.IsRunning)
        {
            _stopwatch.Stop();
        }

        Console.WriteLine("\n\n*** ALL MESSAGES RECEIVED! ***\n");
        PrintFinalResults(expectedMessages + warmupMessages, _receivedCount);

        // Force exit after 2 seconds to allow user to see results
        Thread.Sleep(2000);
        Environment.Exit(0);
    }

    static void PrintFinalResults(int expectedReceived, long actualReceived)
    {
        Console.WriteLine("\n========== SUBSCRIBER PERFORMANCE RESULTS ==========");
        Console.WriteLine($"Expected Messages:       {expectedReceived:N0}");
        Console.WriteLine($"Messages Received:       {actualReceived:N0}");
        Console.WriteLine($"Message Loss:            {expectedReceived - actualReceived:N0} " +
                          $"({(1 - (double)actualReceived / expectedReceived) * 100:F2}%)");

        if ((actualReceived > 0) &&
            (_stopwatch.Elapsed.TotalSeconds > 0))
        {
            double totalSeconds = _stopwatch.Elapsed.TotalSeconds;
            double messagesPerSecond = actualReceived / totalSeconds;
            double microsecondsPerMessage = (_stopwatch.Elapsed.TotalMilliseconds * 1000) / actualReceived;

            Console.WriteLine($"\nTotal Time:              {totalSeconds:F3} seconds");
            Console.WriteLine($"Messages per Second:     {messagesPerSecond:N0} msg/s");
            Console.WriteLine($"Microseconds per Message: {microsecondsPerMessage:F2} μs");
            Console.WriteLine($"Total Elapsed:           {_stopwatch.Elapsed}");

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
