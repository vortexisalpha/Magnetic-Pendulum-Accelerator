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
    public int[] pixels;
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

    [Header("PYNQ board TCP endpoint")]
    [SerializeField] private string host = "192.168.2.99";
    [SerializeField] private int port = 12345;
    [Tooltip("Seconds to wait before retrying a dropped/refused connection.")]
    [SerializeField] private float reconnectInterval = 2f;

    //protocol constants
    private const byte MSG_PARAMS = 0x01;
    private const byte MSG_MAGNETS = 0x02;
    private const byte MSG_VIEWPORT = 0x03;
    private const byte MSG_TRAJ_REQ = 0x04;
    private const byte MSG_IMAGE = 0x10;
    private const byte MSG_INFO = 0x11;
    private const byte MSG_TRAJ = 0x14;
    private const int IMAGE_HEADER = 9; //u16 w + u16 h + u8 depth + u32 version
    private const int TRAJ_HEADER = 6; //u32 pixel_id + u16 n

    //events are always raised on the Unity main thread
    public event Action<ImageMessage> ImageReceived;
    public event Action<InfoMessage> InfoReceived;
    public event Action<TrajectoryMessage> TrajectoryReceived;

    public ImageMessage LatestImage { get; private set; }
    public InfoMessage LatestInfo { get; private set; }
    public TrajectoryMessage LatestTrajectory { get; private set; }
    public bool IsConnected => connected;

    //pixel id of the most recent trajectory request, used to verify echoed replies
    public uint LastRequestedTrajectoryPixelId { get; private set; }
    public bool HasRequestedTrajectory { get; private set; }

    //ids we are still waiting on; replies are matched against this so rapid clicks
    //don't drop the earlier (still valid) trajectories. accessed on main thread only.
    private readonly HashSet<uint> pendingTrajectoryRequests = new HashSet<uint>();

    //when true the board renders the final-state-sensitivity map instead of the
    //basin; carried as an extra field on the existing PARAMS frame (no new event)
    public bool FssMode { get; private set; }

    public void SetFssMode(bool on) => FssMode = on;

    public int LatestSentParamVersion { get; private set; }

    public int MinAcceptedImageVersion { get; private set; }

    public event Action Connected;

    //threading
    private Thread ioThread;
    private volatile bool running;
    private volatile bool connected;

    private TcpClient client;
    private NetworkStream stream;
    private readonly object writeLock = new object();

    private readonly ConcurrentQueue<object> inbound = new ConcurrentQueue<object>();

    private float lastSentDampingFactor, lastSentMagneticStrength, lastSentPendulumLength, lastSentPendulumHeight;
    private bool lastSentFss;
    private int lastSentResX, lastSentResY;
    private int renderRequestId;
    private bool hasSentParams;
    private volatile bool notifyConnected;

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
        if (notifyConnected)
        {
            notifyConnected = false;
            MinAcceptedImageVersion = 0;
            PendulumRenderer.ResetFetchedVersion();
            Connected?.Invoke();
        }

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
                case TrajectoryMessage traj:
                    LatestTrajectory = traj;
                    TrajectoryReceived?.Invoke(traj);
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

    public void SendParams(float dampingFactor, float magneticStrength, float pendulumLength, float pendulumHeight,
                           int resX, int resY)
    {
        bool fss = FssMode;

        const int minResolution = 12;
        if (resX < minResolution) resX = minResolution;
        if (resY < minResolution) resY = minResolution;

        if (hasSentParams &&
            dampingFactor == lastSentDampingFactor &&
            magneticStrength == lastSentMagneticStrength &&
            pendulumLength == lastSentPendulumLength &&
            pendulumHeight == lastSentPendulumHeight &&
            fss == lastSentFss &&
            resX == lastSentResX &&
            resY == lastSentResY)
            return;

        int paramVersion = LatestSentParamVersion + 1;
        int minImageVersion = PendulumRenderer.LastFetchedVersion;
        int requestId = ++renderRequestId;

        string json = JsonConvert.SerializeObject(new
        {
            version = paramVersion,
            requestId,
            dampingFactor,
            magneticStrength,
            pendulumLength,
            pendulumHeight,
            fss,
            resX,
            resY
        });
        byte[] frame = BuildFrame(MSG_PARAMS, Encoding.UTF8.GetBytes(json));
        Debug.Log($"[Pynq] send PARAMS v{paramVersion} req={requestId}: {json}");

        if (!WriteFrame(frame))
            return;

        lastSentDampingFactor = dampingFactor;
        lastSentMagneticStrength = magneticStrength;
        lastSentPendulumLength = pendulumLength;
        lastSentPendulumHeight = pendulumHeight;
        lastSentFss = fss;
        lastSentResX = resX;
        lastSentResY = resY;
        hasSentParams = true;
        LatestSentParamVersion = paramVersion;
        MinAcceptedImageVersion = minImageVersion;
    }

    public void SendMagnets(Dictionary<string, MagnetCoords> magnets)
    {
        if (magnets == null) return;
        string json = JsonConvert.SerializeObject(new { magnets });
        WriteFrame(BuildFrame(MSG_MAGNETS, Encoding.UTF8.GetBytes(json)));
    }

    public void SendViewport(float xMin, float xMax, float yMin, float yMax)
    {
        Debug.Log("PynqConnection - sending viewport...");
        string json = JsonConvert.SerializeObject(new
        {
            x_min = xMin,
            x_max = xMax,
            y_min = yMin,
            y_max = yMax
        });
        WriteFrame(BuildFrame(MSG_VIEWPORT, Encoding.UTF8.GetBytes(json)));
    }

    //0x04 TRAJ_REQ: ask the board for the trajectory starting at the chosen pixel
    public void SendTrajectoryRequest(uint pixelId)
    {
        LastRequestedTrajectoryPixelId = pixelId;
        HasRequestedTrajectory = true;
        pendingTrajectoryRequests.Add(pixelId);
        string json = JsonConvert.SerializeObject(new { pixel_id = pixelId });
        WriteFrame(BuildFrame(MSG_TRAJ_REQ, Encoding.UTF8.GetBytes(json)));
    }

    //true if pixelId was actually requested; clears it from the pending set so we
    //only render trajectories the user asked for (guards against wrong-pixel data)
    public bool TryConsumeTrajectoryRequest(uint pixelId)
    {
        return pendingTrajectoryRequests.Remove(pixelId);
    }

    private bool WriteFrame(byte[] frame)
    {
        if (!connected)
            return false;

        try
        {
            lock (writeLock)
                stream.Write(frame, 0, frame.Length);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Pynq] send failed: {e.Message}");
            return false;
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
                notifyConnected = true;

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
                case MSG_TRAJ:
                    inbound.Enqueue(DecodeTrajectory(payload));
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

    //0x14 TRAJ: u32 pixel_id, u16 n, then n x (f32 x, f32 y), all big-endian
    private static TrajectoryMessage DecodeTrajectory(byte[] p)
    {
        uint pixelId = ReadU32BE(p, 0);
        int n = ReadU16BE(p, 4);

        var points = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            int o = TRAJ_HEADER + i * 8;
            points[i] = new Vector2(ReadF32BE(p, o), ReadF32BE(p, o + 4));
        }

        return new TrajectoryMessage { pixelId = pixelId, points = points };
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

    //reinterpret a big-endian 4-byte float regardless of host endianness
    private static float ReadF32BE(byte[] b, int o)
    {
        uint bits = ReadU32BE(b, o);
        return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
    }
}
