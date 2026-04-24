using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;

using Faster.Transport;
using Faster.Transport.Contracts;



namespace Faster.Transport.Ipc.Sample.Net9.Client05Perf;

public class Program
{
    #region Configuration
    /// <summary>
    /// Channel name by default is equal to Server05Perf.Program.IpcChannelName.
    /// You can change it with command line argument.
    /// This makes this Client compatible with Server07Perf (at least with this one).
    /// </summary>
    public const string IpcChannelName = Faster.Transport.Ipc.Sample.Net9.Server05Perf.Program.IpcChannelName; // "IpcServer05Perf";

    /// <summary>
    /// Expected regular messages: 10_000_000
    /// </summary>
    public const int expectedMessages = Faster.Transport.Ipc.Sample.Net9.Server05Perf.Program.totalMessages; // 10_000_000;
    /// <summary>
    /// Warmup messages: 100_000
    /// </summary>
    public const int warmupMessages = Faster.Transport.Ipc.Sample.Net9.Server05Perf.Program.warmupMessages; // 100_000; // Warmup to stabilize performance
    /// <summary>
    /// Progress log interval: 1_000_000
    /// </summary>
    public const long progressInterval = 1_000_000;
    #endregion Configuration

    private static long _receivedCount = 0;
    private static long _lastProgressCount = 0;
    private static readonly object _lockObj = new object();
    private static readonly Stopwatch _stopwatch = new Stopwatch();

    private static readonly ConcurrentQueue<long> _latencies = new ConcurrentQueue<long>();
    private static readonly ManualResetEvent _completionEvent = new ManualResetEvent(false);

    static void Main(string[] args)
    {
        Console.WriteLine();
        Console.WriteLine("Starting LATENCY MEASURING SUBSCRIBER (SYNC)...");

        // Ensure high priority for this thread
        Thread.CurrentThread.Priority = ThreadPriority.Highest;
        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;

        //// Monitor completion
        //var monitorThread = new Thread(() => MonitorCompletion(expectedMessages));
        //monitorThread.Start();

        // Let's do step-by-step:
        string channelName = IpcChannelName;
        if (args.Length > 0)
            channelName = args[0];

        ParticleBuilder builder = new ParticleBuilder();
        builder = builder.UseMode(TransportMode.Ipc); // Must match the publisher
        builder = builder.WithChannel(channelName);
        builder = builder.WithGlobal(false);
        builder = builder.OnReceived(SubscriberOnReceived);
        IParticle client = builder.Build();
        Console.WriteLine("   This client is of type     : {0}", client.GetType().FullName);
        Console.WriteLine("   Base name of the channel is: {0}", channelName);
        Console.WriteLine();

        Console.WriteLine($"Subscriber started. Expecting {expectedMessages:N0} messages...\n");



        // Wait for completion (synchronous wait)
        Console.WriteLine("Waiting for all messages...");
        _completionEvent.WaitOne();

        Thread.Sleep(300);



        // Ensure stopwatch is stopped
        if (_stopwatch.IsRunning)
        {
            _stopwatch.Stop();
        }

        PrintFinalResults(expectedMessages, _receivedCount);

        // Calculate latency statistics
        List<long> latencyList = new List<long>(Math.Max(16, _latencies.Count));
        while (_latencies.Count > 0)
        {
            if (_latencies.TryDequeue(out long t))
                latencyList.Add(t);
        }
        if (latencyList.Count > 0)
        {
            latencyList.Sort();
        }

        Console.WriteLine("\n========== FINAL PERFORMANCE RESULTS ==========");
        Console.WriteLine($"Total Messages:          {_receivedCount:N0}");
        Console.WriteLine($"Total Time:              {_stopwatch.Elapsed.TotalSeconds:F3} seconds");
        Console.WriteLine($"Messages per Second:     {_receivedCount / _stopwatch.Elapsed.TotalSeconds:N0} msg/s");

        if (latencyList.Count > 0)
        {
            double avgLatency = 0;
            foreach (long lat in latencyList)
                avgLatency += lat;
            avgLatency /= latencyList.Count;
            
            double p50Latency = latencyList[latencyList.Count / 2];
            double p95Latency = latencyList[(int)(latencyList.Count * 0.95)];
            double p99Latency = latencyList[(int)(latencyList.Count * 0.99)];
            double p999Latency = latencyList[(int)(latencyList.Count * 0.999)];

            Console.WriteLine("\n--- Latency Statistics (microseconds) ---");
            Console.WriteLine($"Average Latency:        {avgLatency:F2} us");
            Console.WriteLine($"P50 Latency:            {p50Latency:F2} us");
            Console.WriteLine($"P95 Latency:            {p95Latency:F2} us");
            Console.WriteLine($"P99 Latency:            {p99Latency:F2} us");
            Console.WriteLine($"P99.9 Latency:          {p999Latency:F2} us");
            Console.WriteLine($"Samples Collected:      {latencyList.Count:N0}");
        }
        else
        {
            Console.WriteLine("\n--- Latency Statistics ---");
            Console.WriteLine("No latency samples collected.");
        }

        Console.WriteLine("===============================================\n");



        client.Dispose();
    }

    private static void SubscriberOnReceived(IParticle particle, ReadOnlyMemory<byte> memory)
    {
        //long receiveTimestamp = Stopwatch.GetTimestamp();
        //long receiveTicks = timeProv.UtcNowTicks;
        long receiveTicks = Stopwatch.GetTimestamp();
        long currentCount = Interlocked.Increment(ref _receivedCount);

        // Start timing on first message
        if (currentCount == warmupMessages + 1)
        {
            lock (_stopwatch)
            {
                if (!_stopwatch.IsRunning)
                {
                    _stopwatch.Start();

                    // Clear latencies of warm-up phase
                    _latencies.Clear();
                }
            }

            string text = Encoding.UTF8.GetString(memory.Span);
            Console.WriteLine("    First msg:'" + text + "';   receiveTicks:" + receiveTicks);
        }

        // Parse message to get sequence number (no string allocation)
        int messageNumber = 0;
        ReadOnlySpan<byte> span = memory.Span;

        // Fast manual parsing of message ID
        int idx = 0;
        while ((idx < span.Length) && (span[idx] != (byte)'|'))
        {
            if (span[idx] >= (byte)'0' && span[idx] <= (byte)'9')
            {
                messageNumber = messageNumber * 10 + (span[idx] - (byte)'0');
            }
            idx++;
        }

        // Пропускаем разделитель '|'
        idx++;

        long sendTicks = 0;
        while (idx < span.Length)
        {
            if (span[idx] >= (byte)'0' && span[idx] <= (byte)'9')
            {
                sendTicks = sendTicks * 10 + (span[idx] - (byte)'0');
            }
            idx++;
        }

        // Calculate latency (simplified - you'd need to pass timestamps through messages)
        // For true latency measurement, you'd send the timestamp in the message
        long latencyTicks = receiveTicks - sendTicks;
        //double latencyUs = (latencyTicks * 1_000_000.0) / _frequency;
        double latencyUs = 0.1 * latencyTicks;

        // Sample latencies (only store 0.1% to avoid memory issues)
        if (currentCount % 1_000 == 0)
        {
            _latencies.Enqueue((long)Math.Ceiling(latencyUs));
        }

        // Progress reporting (thread-safe, lock-free)
        long currentProgress = currentCount / progressInterval;
        if ((currentProgress > _lastProgressCount) &&
            (currentCount % progressInterval == 0))
        {
            Interlocked.Exchange(ref _lastProgressCount, currentProgress);
            double elapsedSec = _stopwatch.Elapsed.TotalSeconds;
            double currentRate = currentCount / elapsedSec;
            Console.WriteLine($"  Received: {currentCount:N0} messages - Rate: {currentRate:N0} msg/s");
        }

        // Signal completion when done
        if (currentCount >= expectedMessages + warmupMessages)
        {
            _completionEvent.Set();
        }

        if (currentCount == expectedMessages + warmupMessages)
        {
            string text = Encoding.UTF8.GetString(memory.Span);
            Console.WriteLine("    Last msg :'" + text + "';   receiveTicks:" + receiveTicks);
        }
    }

    //static void MonitorCompletion(int expectedMessages)
    //{
    //    while (_receivedCount < expectedMessages)
    //    {
    //        Thread.Sleep(100); // Check every 100ms
    //    }

    //    // All messages received
    //    if (_stopwatch.IsRunning)
    //    {
    //        _stopwatch.Stop();
    //    }

    //    Console.WriteLine("\n\n*** ALL MESSAGES RECEIVED! ***\n");
    //    PrintFinalResults(expectedMessages + warmupMessages, _receivedCount);

    //    // Force exit after 2 seconds to allow user to see results
    //    Thread.Sleep(2_000);
    //    //Environment.Exit(0);
    //}

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
            Console.WriteLine($"Microseconds per Message: {microsecondsPerMessage:F2} us");
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
