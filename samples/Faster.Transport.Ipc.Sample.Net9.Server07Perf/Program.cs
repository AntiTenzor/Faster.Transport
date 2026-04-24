using System;
using System.Text;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Faster.Transport;
using Faster.Transport.Contracts;



namespace Faster.Transport.Ipc.Sample.Net9.Server07Perf;

public unsafe class Program
{
    // Configuration
    public const string IpcChannelName = "IpcServer07Perf";

    public const int totalMessages = 10_000_000;
    public const int batchSize = 1_000; // Send in batches to reduce overhead
    public const int warmupMessages = 100_000; // Warmup to stabilize performance

    //// Pre-allocated buffer pool for zero-allocation messaging
    //private static readonly byte[] _sharedBuffer = new byte[256];
    ////private static readonly char[] _numberBuffer = new char[20]; // Max for 64-bit int

    static void Main(string[] args)
    {
        Console.WriteLine();
        Console.WriteLine("Starting ENHANCED IPC PUBLISHER (SYNC) with detailed metrics...");

        // Ensure high priority for this thread
        System.Threading.Thread.CurrentThread.Priority = System.Threading.ThreadPriority.Highest;
        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;

        // Let's do step-by-step:
        string channelName = IpcChannelName;
        if (args.Length > 0)
            channelName = args[0];

        ReactorBuilder builder = new ReactorBuilder();
        builder = builder.UseMode(TransportMode.Ipc); // Use Shared Memory
        builder = builder.WithChannel(channelName);
        builder = builder.WithGlobal(false);
        builder = builder.OnConnected(SeverOnConnected);
        IReactor server = builder.Build();
        Console.WriteLine("   This server is of type     : {0}", server.GetType().FullName);
        Console.WriteLine("   Base name of the channel is: {0}", channelName);
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
        // Pre-allocated buffer pool for zero-allocation messaging
        byte[] sharedBuffer = new byte[256];

        // Warmup phase
        Console.WriteLine();
        Console.WriteLine($"Warming up with {warmupMessages:N0} messages...");
        PublishZeroAlloc(client, warmupMessages, batchSize, isWarmup: true, sharedBuffer);

        // Clear timestamps for actual measurement
        //sendTimestamps.Clear();
        GC.Collect();
        GC.WaitForPendingFinalizers();

        // Actual measurement phase
        Console.WriteLine($"Starting actual measurement for {totalMessages:N0} messages...");
        var stopwatch = Stopwatch.StartNew();

        long messagesSent = PublishZeroAlloc(client, totalMessages, batchSize, isWarmup: false, sharedBuffer);

        stopwatch.Stop();

        // Calculate statistics
        double totalSeconds = stopwatch.Elapsed.TotalSeconds;
        double messagesPerSecond = messagesSent / totalSeconds;
        double microsecondsPerMessage = (stopwatch.Elapsed.TotalMilliseconds * 1000) / messagesSent;

        Console.WriteLine("\n========== PUBLISHER PERFORMANCE RESULTS ==========");
        Console.WriteLine($"Total Messages Sent:     {messagesSent:N0}");
        Console.WriteLine($"Total Time:              {stopwatch.Elapsed.TotalSeconds:F3} seconds");
        Console.WriteLine($"Messages per Second:     {messagesPerSecond:N0} msg/s");
        Console.WriteLine($"Microseconds per Message: {microsecondsPerMessage:F2} us");
        Console.WriteLine($"Total Elapsed:           {stopwatch.Elapsed}");
        Console.WriteLine("===================================================\n");
    }

    static long PublishZeroAlloc(IParticle client, int messageCount, int batchSize, bool isWarmup,
        byte[] externalBuffer)
    {
        long sentCount = 0;
        var stopwatch = new Stopwatch();

        if (!isWarmup)
        {
            stopwatch.Start();
        }

        int length = 0;
        fixed (byte* bufferPtr = externalBuffer)
        //fixed (char* numberPtr = _numberBuffer)
        {
            for (int i = 0; i < messageCount; i += batchSize)
            {
                int currentBatchSize = Math.Min(batchSize, messageCount - i);

                for (int j = 0; j < currentBatchSize; j++)
                {
                    int messageNumber = i + j;

                    // Avoid formatted strings in 'HighPerf' code

                    // Fast zero-allocation number formatting
                    length = FastIntToBytes(messageNumber, bufferPtr);

                    // Add separator
                    bufferPtr[length++] = (byte)'|';

                    // Add timestamp (could be optimized further)
                    //long timestamp = timeProv.UtcNowTicks;
                    long timestamp = Stopwatch.GetTimestamp();
                    length += FastLongToBytes(timestamp, bufferPtr + length);

                    // Send synchronously using span
                    client.Send(new Span<byte>(bufferPtr, length));
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
        } // End fixed (...)

        if (!isWarmup)
        {
            stopwatch.Stop();
        }

        string text = Encoding.UTF8.GetString(externalBuffer, 0, length);
        Console.WriteLine("    Last msg :'" + text + "'");

        return sentCount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static unsafe int FastIntToBytes(int value, byte* buffer)
    {
        // Fast conversion for numbers 0-999,999,999
        if (value < 10)
        {
            buffer[0] = (byte)('0' + value);
            return 1;
        }

        int length = 0;
        int temp = value;

        // Count digits
        do
        {
            temp /= 10;
            length++;
        } while (temp > 0);

        // Write digits from right to left
        temp = value;
        for (int i = length - 1; i >= 0; i--)
        {
            buffer[i] = (byte)('0' + (temp % 10));
            temp /= 10;
        }

        return length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static unsafe int FastLongToBytes(long value, byte* buffer)
    {
        if (value == 0)
        {
            buffer[0] = (byte)'0';
            return 1;
        }

        int length = 0;
        long temp = value;

        // Count digits
        do
        {
            temp /= 10;
            length++;
        } while (temp > 0);

        // Write digits from right to left
        temp = value;
        for (int i = length - 1; i >= 0; i--)
        {
            buffer[i] = (byte)('0' + (temp % 10));
            temp /= 10;
        }

        return length;
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
