using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;
using UnityEngine;
using Newtonsoft.Json;

public sealed class ImageMessage
{
    public int width;
    public int height;
    public int bitDepth;
    public int version;
    public int[] pixels;   // flat, length width*height, row-major (pixels[y*width + x])
}

public sealed class MagnetCoords
{
    public float x;
    public float y;
}

public sealed class InfoMessage
{
    public Dictionary<string, MagnetCoords> magnets;
}

public class PynqConnection : MonoBehaviour
{
    public static PynqConnection Instance { get; private set; }

    [Header("PYNQ board endpoint")]
    [SerializeField] private string host = "192.168.2.99";
    [SerializeField] private int port = 12345;
    [Tooltip("Seconds to wait before retrying a dropped/refused connection.")]
    [SerializeField] private float reconnectInterval = 2f;

    //protocol constants
    private const byte MSG_PARAMS = 0x01;
    private const byte MSG_MAGNETS = 0x02;
    private const byte MSG_IMAGE = 0x10;
    private const byte MSG_INFO = 0x11;
    private const int IMAGE_HEADER = 9; //u16 w + u16 h + u8 depth + u32 version

    //events are always raised on the Unity main thread
    public event Action<ImageMessage> ImageReceived;
    public event Action<InfoMessage> InfoReceived;

    public ImageMessage LatestImage { get; private set; }
    public InfoMessage LatestInfo { get; private set; }
    public bool IsConnected => connected;

    /// <summary>Monotonic counter bumped on each flushed PARAMS frame.</summary>
    public int LatestSentParamVersion { get; private set; }

    /// <summary>Drop IMAGE frames at or below this FPGA version (set when params are sent).</summary>
    public int MinAcceptedImageVersion { get; private set; }

    //threading
    private Thread ioThread;
    private volatile bool running;
    private volatile bool connected;

    private TcpClient client;
    private NetworkStream stream;
    private readonly object writeLock = new object();

    private readonly ConcurrentQueue<object> inbound = new ConcurrentQueue<object>();
    private readonly ConcurrentQueue<byte[]> outbound = new ConcurrentQueue<byte[]>();
    private byte[] pendingParamsFrame;
    private float pendingDampingFactor, pendingMagneticStrength, pendingPendulumLength, pendingPendulumHeight;
    private int pendingParamVersion;
    private int pendingMinAcceptedImageVersion;
    private readonly object outboundLock = new object();

    private float lastSentDampingFactor, lastSentMagneticStrength, lastSentPendulumLength, lastSentPendulumHeight;
    private bool hasSentParams;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        running = true;
        ioThread = new Thread(IoLoop) { IsBackground = true, Name = "PynqIO" };
        ioThread.Start();
    }

    void Update()
    {
        while (inbound.TryDequeue(out object msg))
        {
            switch (msg)
            {
                case ImageMessage img:
                    LatestImage = img;
                    ImageReceived?.Invoke(img);
                    break;
                case InfoMessage info:
                    LatestInfo = info;
                    InfoReceived?.Invoke(info);
                    break;
            }
        }
    }

    void OnDestroy()
    {
        running = false;
        try { stream?.Close(); } catch { }
        try { client?.Close(); } catch { }
        if (Instance == this) Instance = null;
    }

    //outgoing: slider params coalesce into a single pending frame (latest wins)
    public void SendParams(float dampingFactor, float magneticStrength, float pendulumLength, float pendulumHeight)
    {
        if (hasSentParams &&
            dampingFactor == lastSentDampingFactor &&
            magneticStrength == lastSentMagneticStrength &&
            pendulumLength == lastSentPendulumLength &&
            pendulumHeight == lastSentPendulumHeight)
            return;

        lock (outboundLock)
        {
            if (pendingParamsFrame != null &&
                dampingFactor == pendingDampingFactor &&
                magneticStrength == pendingMagneticStrength &&
                pendulumLength == pendingPendulumLength &&
                pendulumHeight == pendingPendulumHeight)
                return;
        }

        int paramVersion = LatestSentParamVersion + 1;
        int minImageVersion = PendulumRenderer.LastFetchedVersion;

        string json = JsonConvert.SerializeObject(new
        {
            version = paramVersion,
            dampingFactor,
            magneticStrength,
            pendulumLength,
            pendulumHeight
        });
        byte[] frame = BuildFrame(MSG_PARAMS, Encoding.UTF8.GetBytes(json));

        lock (outboundLock)
        {
            pendingParamsFrame = frame;
            pendingDampingFactor = dampingFactor;
            pendingMagneticStrength = magneticStrength;
            pendingPendulumLength = pendulumLength;
            pendingPendulumHeight = pendulumHeight;
            pendingParamVersion = paramVersion;
            pendingMinAcceptedImageVersion = minImageVersion;
        }

        FlushOutbound();
    }

    public void SendMagnets(Dictionary<string, MagnetCoords> magnets)
    {
        if (magnets == null) return;
        string json = JsonConvert.SerializeObject(new { magnets });
        SendJson(MSG_MAGNETS, json);
    }

    private void SendJson(byte type, string json)
    {
        outbound.Enqueue(BuildFrame(type, Encoding.UTF8.GetBytes(json)));
        FlushOutbound();
    }

    private void FlushOutbound()
    {
        if (!connected) return;

        try
        {
            lock (writeLock)
            {
                lock (outboundLock)
                {
                    if (pendingParamsFrame != null)
                    {
                        stream.Write(pendingParamsFrame, 0, pendingParamsFrame.Length);
                        lastSentDampingFactor = pendingDampingFactor;
                        lastSentMagneticStrength = pendingMagneticStrength;
                        lastSentPendulumLength = pendingPendulumLength;
                        lastSentPendulumHeight = pendingPendulumHeight;
                        hasSentParams = true;
                        LatestSentParamVersion = pendingParamVersion;
                        MinAcceptedImageVersion = pendingMinAcceptedImageVersion;
                        pendingParamsFrame = null;
                    }
                }

                while (outbound.TryDequeue(out byte[] frame))
                    stream.Write(frame, 0, frame.Length);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Pynq] send failed: {e.Message}");
        }
    }

    private static byte[] BuildFrame(byte type, byte[] payload)
    {
        byte[] frame = new byte[5 + payload.Length];
        frame[0] = type;
        WriteU32BE(frame, 1, (uint)payload.Length);
        Buffer.BlockCopy(payload, 0, frame, 5, payload.Length);
        return frame;
    }

    private void IoLoop()
    {
        while (running)
        {
            try
            {
                client = new TcpClient();
                client.NoDelay = true;
                client.Connect(host, port);
                stream = client.GetStream();
                connected = true;
                Debug.Log($"[Pynq] connected to {host}:{port}");
                FlushOutbound();

                ReadFrames();
            }
            catch (Exception e)
            {
                if (running) Debug.LogWarning($"[Pynq] connection error: {e.Message}");
            }
            finally
            {
                connected = false;
                hasSentParams = false;
                lock (outboundLock)
                    pendingParamsFrame = null;
                while (outbound.TryDequeue(out _)) { }
                try { stream?.Close(); } catch { }
                try { client?.Close(); } catch { }
            }

            if (running) Thread.Sleep((int)(reconnectInterval * 1000));
        }
    }

    private void ReadFrames()
    {
        byte[] header = new byte[5];
        while (running)
        {
            ReadExactly(header, 5);
            byte type = header[0];
            int length = (int)ReadU32BE(header, 1);

            byte[] payload = length > 0 ? new byte[length] : Array.Empty<byte>();
            if (length > 0) ReadExactly(payload, length);

            switch (type)
            {
                case MSG_IMAGE:
                    inbound.Enqueue(DecodeImage(payload));
                    break;
                case MSG_INFO:
                    inbound.Enqueue(DecodeInfo(payload));
                    break;
                default:
                    Debug.LogWarning($"[Pynq] unknown frame type 0x{type:X2} ({length} bytes)");
                    break;
            }
        }
    }

    private void ReadExactly(byte[] buffer, int count)
    {
        int read = 0;
        while (read < count)
        {
            int n = stream.Read(buffer, read, count - read);
            if (n <= 0) throw new System.IO.EndOfStreamException("peer closed");
            read += n;
        }
    }

    private static ImageMessage DecodeImage(byte[] p)
    {
        int width = ReadU16BE(p, 0);
        int height = ReadU16BE(p, 2);
        int bitDepth = p[4];
        int version = (int)ReadU32BE(p, 5);

        int count = width * height;
        int[] pixels = new int[count];

        if (bitDepth <= 8)
        {
            for (int i = 0; i < count; i++)
                pixels[i] = p[IMAGE_HEADER + i];
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                int o = IMAGE_HEADER + i * 2;
                pixels[i] = (p[o] << 8) | p[o + 1];
            }
        }

        return new ImageMessage
        {
            width = width,
            height = height,
            bitDepth = bitDepth,
            version = version,
            pixels = pixels
        };
    }

    private static InfoMessage DecodeInfo(byte[] p)
    {
        string json = Encoding.UTF8.GetString(p);
        var info = JsonConvert.DeserializeObject<InfoMessage>(json);
        if (info.magnets == null)
            info.magnets = new Dictionary<string, MagnetCoords>();
        return info;
    }

    private static void WriteU32BE(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24);
        b[o + 1] = (byte)(v >> 16);
        b[o + 2] = (byte)(v >> 8);
        b[o + 3] = (byte)v;
    }

    private static uint ReadU32BE(byte[] b, int o) =>
        ((uint)b[o] << 24) | ((uint)b[o + 1] << 16) | ((uint)b[o + 2] << 8) | b[o + 3];

    private static int ReadU16BE(byte[] b, int o) => (b[o] << 8) | b[o + 1];
}
