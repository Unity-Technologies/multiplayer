using UnityEngine;

public class SoakClientBehaviour : MonoBehaviour
{
    [SerializeField] public int SoakClientCount;
    [SerializeField] public int SoakPacketsPerSecond;
    [SerializeField] public int SoakPacketSize;
    [SerializeField] public int SoakDuration;
    [SerializeField] public int SampleTime;

    private SoakClientJobManager m_Manager;
    private float m_SampleExpiresTime;
    private bool m_Running;
    private float m_StartTime;

    private StatisticsReport m_Report;

    void Start()
    {
        m_Manager = new SoakClientJobManager(SoakClientCount, SoakPacketsPerSecond, SoakPacketSize, SoakDuration);
        m_Report = new StatisticsReport(SoakClientCount);
    }

    void OnDestroy()
    {
        m_Manager.Dispose();
        m_Report.Dispose();
    }

    void FixedUpdate()
    {
        if (!m_Running)
            return;

        m_Manager.Sync();
        if (SampleExpired())
        {
            var now = Time.time;
            var sample = m_Manager.Sample();
            for (int i = 0; i < sample.Length; i++)
            {
                m_Report.AddSample(sample[i], now);
            }
        }

        if (m_Manager.Done())
        {
            Debug.Log("Soak done!");
            m_Running = false;
            Log();
        }

        m_Manager.Update();
    }

    void StartSoak()
    {
        m_Running = true;
        m_SampleExpiresTime = Time.time + SampleTime;
        m_Manager.Start();
        m_StartTime = Time.time;
    }

    void StopSoak()
    {
        m_Running = false;
        m_Manager.Stop();
    }

    bool SampleExpired()
    {
        if (!m_Running)
            return false;

        bool expired = false;
        var now = Time.time;
        if (now > m_SampleExpiresTime)
        {
            expired = true;
            m_SampleExpiresTime = now + SampleTime;
        }
        return expired;
    }

    void Log()
    {
        var gen = new SoakStatisticsReporter();
        gen.GenerateReport(m_Report, m_Manager.ClientInfos());
        /*
        MemoryStream ms = new MemoryStream();
        DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(SoakStatisticsReport));
        ser.WriteObject(ms, report);

        byte[] json = ms.ToArray();
        ms.Close();
        Debug.Log(Encoding.UTF8.GetString(json, 0, json.Length));
        */
    }

    void OnGUI()
    {
        if (!m_Running)
        {
            if (GUILayout.Button("Start soaking"))
            {
                StartSoak();
            }
        }
        else
        {
            GUILayout.Label((int)(Time.time - m_StartTime) + " seconds elapsed");
            if (GUILayout.Button("Stop soaking"))
            {
                StopSoak();
            }
        }

        /*
        foreach (var client in m_SoakClients)
        {
            //GUILayout.Label("PING " + client.SoakJobStatisticsHandle[0].FrameId + ": " + client.SoakJobStatisticsHandle[0].PingTime + "ms");
            if (!client.ServerEndPoint.IsValid)
            {
                if (GUILayout.Button("Start ping"))
                {
                    if (string.IsNullOrEmpty(client.CustomIp))
                        client.ServerEndPoint = new IPEndPoint(IPAddress.Loopback, 9000);
                    else
                    {
                        client.ServerEndPoint = new IPEndPoint(IPAddress.Parse(client.CustomIp), 9000);
                    }
                }
                client.CustomIp = GUILayout.TextField(client.CustomIp);
            }
            else
            {
                if (GUILayout.Button("Stop ping"))
                {
                    client.ServerEndPoint = default(NetworkEndPoint);
                }
            }
        }
        */
    }
}
