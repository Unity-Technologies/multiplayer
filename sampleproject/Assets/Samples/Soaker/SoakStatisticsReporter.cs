using System.Globalization;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEngine;

public class SoakStatisticsReporter
{
    private string header =
        @"<html>
           <head>
            <script type='text/javascript' src='https://www.gstatic.com/charts/loader.js'></script>
            <script type='text/javascript'>
            google.charts.load('current', { 'packages': ['corechart'] });";

    private string footer =
        @"  </script>
            </head>
            <body>
            </body>
            </html>";

    struct ChartData
    {
        public int Columns;
        public int Rows;
        public NativeArray<double> Points;

        public ChartData(int columns, int rows)
        {
            Columns = columns;
            Rows = rows;
            Points = new NativeArray<double>(rows * columns, Allocator.Persistent);
        }
    }

    public void GenerateReport(StatisticsReport report, string[] clientInfos)
    {
        var length = report.Samples.Length;
        var columns = report.BucketSize + 1;
        var rows = length / report.BucketSize;

        var ptm_data = new ChartData(columns, rows); // PingTimeMean Received;
        var pr_data = new ChartData(columns, rows); // Packets Received;
        var ps_data = new ChartData(columns, rows); // Packets Sent;
        var pd_data = new ChartData(columns, rows); // Packets Dropped or Stale;
        var br_data = new ChartData(columns, rows); // Bytes Received;
        var bs_data = new ChartData(columns, rows); // Bytes Sent;

        var relsent_data = new ChartData(columns, rows);
        var relresent_data = new ChartData(columns, rows);
        var reldrop_data = new ChartData(columns, rows);
        var relrecv_data = new ChartData(columns, rows);
        var reldup_data = new ChartData(columns, rows);
        var relrtt_data = new ChartData(columns, rows);
        var relrtt_queue = new ChartData(columns, rows);
        var relrtt_age = new ChartData(columns, rows);
        var relrtt_max = new ChartData(columns, rows);
        var relrtt_smooth = new ChartData(columns, rows);
        var relrtt_proc = new ChartData(columns, rows);

        int it = 0;
        for (int row = 0; row < rows; row++)
        {
            var timestamp = report.Samples[it].Timestamp;

            ptm_data.Points[row * columns] = timestamp;
            pr_data.Points[row * columns] = timestamp;
            ps_data.Points[row * columns] = timestamp;
            pd_data.Points[row * columns] = timestamp;
            br_data.Points[row * columns] = timestamp;
            bs_data.Points[row * columns] = timestamp;

            relsent_data.Points[row * columns] = timestamp;
            relresent_data.Points[row * columns] = timestamp;
            reldrop_data.Points[row * columns] = timestamp;
            relrecv_data.Points[row * columns] = timestamp;
            reldup_data.Points[row * columns] = timestamp;
            relrtt_data.Points[row * columns] = timestamp;
            relrtt_queue.Points[row * columns] = timestamp;
            relrtt_age.Points[row * columns] = timestamp;
            relrtt_max.Points[row * columns] = timestamp;
            relrtt_smooth.Points[row * columns] = timestamp;
            relrtt_proc.Points[row * columns] = timestamp;

            for (int col = 1; col < columns; col++)
            {
                var sample = report.Samples[it++];
                ptm_data.Points[row * columns + col] = sample.PingTimeMean;
                pr_data.Points[row * columns + col] = sample.ReceivedPackets;
                ps_data.Points[row * columns + col] = sample.SentPackets;
                pd_data.Points[row * columns + col] = sample.DroppedOrStalePackets;
                br_data.Points[row * columns + col] = sample.ReceivedBytes;
                bs_data.Points[row * columns + col] = sample.SentBytes;

                relsent_data.Points[row * columns + col] = sample.ReliableSent;
                relresent_data.Points[row * columns + col] = sample.ReliableResent;
                reldrop_data.Points[row * columns + col] = sample.ReliableDropped;
                relrecv_data.Points[row * columns + col] = sample.ReliableReceived;
                reldup_data.Points[row * columns + col] = sample.ReliableDuplicate;
                relrtt_data.Points[row * columns + col] = sample.ReliableRTT;
                relrtt_queue.Points[row * columns + col] = sample.ReliableResendQueue;
                relrtt_age.Points[row * columns + col] = sample.ReliableOldestResendPacketAge;
                relrtt_max.Points[row * columns + col] = sample.ReliableMaxRTT;
                relrtt_smooth.Points[row * columns + col] = sample.ReliableSRTT;
                relrtt_proc.Points[row * columns + col] = sample.ReliableMaxProcessingTime;
            }
        }

        using (StreamWriter writer = new StreamWriter(UnityEngine.Application.dataPath + "/../soaker_report.html"))
        {
            string ptmds = "Ping Times (Mean)";
            string prds = "Packets Received";
            string psds = "Packets Sent";
            string pdds = "Packets Dropped or Stale";
            string brds = "Bytes Received";
            string bsds = "Bytes Sent";

            writer.Write(header);

            writer.Write(GenerateBody(ptm_data, ptmds, 1, clientInfos));
            writer.Write(GenerateBody(pr_data, prds, 2, clientInfos));
            writer.Write(GenerateBody(ps_data, psds, 3, clientInfos));
            writer.Write(GenerateBody(pd_data, pdds, 4, clientInfos));
            writer.Write(GenerateBody(br_data, brds, 5, clientInfos));
            writer.Write(GenerateBody(bs_data, bsds, 6, clientInfos));

            writer.Write(GenerateBody(relsent_data, "Reliable Sent", 7, clientInfos));
            writer.Write(GenerateBody(relrecv_data, "Reliable Received", 8, clientInfos));
            writer.Write(GenerateBody(relresent_data, "Reliable Resent", 9, clientInfos));
            writer.Write(GenerateBody(reldup_data, "Reliable Duplicate", 10, clientInfos));
            writer.Write(GenerateBody(reldrop_data, "Reliable Dropped", 11, clientInfos));
            writer.Write(GenerateBody(relrtt_data, "Reliable RTT", 12, clientInfos));
            writer.Write(GenerateBody(relrtt_smooth, "Reliable Smooth RTT", 13, clientInfos));
            writer.Write(GenerateBody(relrtt_max, "Reliable Max RTT", 14, clientInfos));
            writer.Write(GenerateBody(relrtt_proc, "Reliable Max Processing Time", 15, clientInfos));
            writer.Write(GenerateBody(relrtt_queue, "Reliable Resend Queue Size", 16, clientInfos));
            writer.Write(GenerateBody(relrtt_age, "Reliable Oldest Resend Packet Age", 17, clientInfos));

            writer.Write(footer);
            writer.Flush();
        }
        ptm_data.Points.Dispose();
        pr_data.Points.Dispose();
        ps_data.Points.Dispose();
        pd_data.Points.Dispose();
        br_data.Points.Dispose();
        bs_data.Points.Dispose();

        reldup_data.Points.Dispose();
        reldrop_data.Points.Dispose();
        relrecv_data.Points.Dispose();
        relsent_data.Points.Dispose();
        relresent_data.Points.Dispose();
        relrtt_data.Points.Dispose();
        relrtt_queue.Points.Dispose();
        relrtt_age.Points.Dispose();
        relrtt_max.Points.Dispose();
        relrtt_smooth.Points.Dispose();
        relrtt_proc.Points.Dispose();
    }

    string GenerateBody(ChartData data, string chartName, int chartId, string[] clientInfos)
    {
        string chartDiv = "chart_div_" + chartId;
        var rows = data.Rows;
        var columns = data.Columns;
        StringBuilder chartData = new StringBuilder(ushort.MaxValue);

        chartData.AppendLine(
            "\n\ngoogle.charts.setOnLoadCallback(displayChart_" + chartId + ")\n" +
            "function displayChart_" + chartId + "()\n" +
            "{\n" +
            "    var data = google.visualization.arrayToDataTable([\n");

        chartData.Append("['Time Elapsed'");
        for (int i = 0; i < columns - 1; i++)
        {
            chartData.Append(", '" + clientInfos[i] + "'");
        }

        chartData.Append("],\n");

        for (int row = 0; row < rows; row++)
        {
            chartData.Append("[" + data.Points[row * columns].ToString(CultureInfo.InvariantCulture));
            for (int col = 1; col < columns; col++)
            {
                chartData.Append(", " + data.Points[row * columns + col].ToString(CultureInfo.InvariantCulture));
            }
            if (row + 1 >= rows)
                chartData.Append("]\n");
            else
                chartData.Append("],\n");
        }

        chartData.Append(
            "])\n" +
            "var name = ' " + chartName + "';\n" +
            "var options = {\n" +
            "    title: name,\n" +
            "    legend: { position: 'bottom' }\n" +
            "};\n" +
            "var div = document.createElement('div');\n" +
            "div.id = '" + chartDiv + "';\n" +
            "div.style.width = '95%';\n" +
            "div.style.height = '640px';\n" +
            "document.body.appendChild(div);\n" +
            "var chart = new google.visualization.LineChart(document.getElementById(div.id));\n" +
            "chart.draw(data, options);\n" +
            "}\n");

        return chartData.ToString();
    }
}
