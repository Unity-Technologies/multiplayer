using System.Collections;
using System.Collections.Generic;
using SQP;
using UnityEngine;

public class SQPTester : MonoBehaviour
{
    void Start()
    {
        m_SQPClient = new SQP.SQPClient(new System.Net.IPEndPoint(0x0100007f, 7777));

        Debug.Log("SQPTester initialized...");
    }

    void Update()
    {
        if(Input.GetKeyDown(KeyCode.Space))
        {
            Debug.Log("Querying server...");
            m_SQPClient.StartInfoQuery();
        }
        m_SQPClient.Update();
    }

    SQP.SQPClient m_SQPClient;
}
