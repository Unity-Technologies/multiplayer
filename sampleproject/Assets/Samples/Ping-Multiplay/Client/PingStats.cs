using System;

namespace MultiplayPingSample.Client
{
	public class PingStats
	{
		byte m_NextIndex;
		byte m_IndicesUsed;
		ushort[] m_RollingWindow;
		uint m_LastRollingAverageTotal;
		double m_LastRollingAverageResult;
        double m_LastPingsPerSecond;
		DateTime m_StartTimer;
        bool m_StopMeasurements;
        uint m_LastUniqueId;

		public PingStats(byte windowSize)
		{
			m_RollingWindow = new ushort[windowSize];
			TotalAverage = 0;
			LastPing = 0;
			TotalPings = 0;
			m_NextIndex = 0;
			m_IndicesUsed = 0;
			m_LastRollingAverageTotal = 0;
			m_LastRollingAverageResult = 0d;
		}

        // Used to prevent spamming
        public void AddEntry(uint sequenceId, ushort latency)
        {
            if (sequenceId == m_LastUniqueId)
                return;

            m_LastUniqueId = sequenceId;
            AddEntry(latency);
        }

		public void AddEntry(ushort latency)
		{
            // Abort if we're in our end state
            if(m_StopMeasurements)
                return;

			LastPing = latency;

            if (LastPing > WorstPing)
                WorstPing = LastPing;

            if (LastPing < BestPing)
                BestPing = LastPing;

			m_RollingWindow[m_NextIndex] = latency;

			if (m_NextIndex == m_RollingWindow.Length - 1)
				m_NextIndex = 0;
			else
				m_NextIndex++;

			if (m_IndicesUsed < m_RollingWindow.Length)
				m_IndicesUsed++;

			// Update total average
			if (TotalPings == 0)
			{
				m_StartTimer = DateTime.UtcNow;
				TotalAverage = latency;
			}
			else
				TotalAverage = TotalAverage / (TotalPings + 1d) * TotalPings + LastPing / (TotalPings + 1d);

			TotalPings++;
		}

		public uint TotalPings { get; private set; }
		public ushort LastPing { get; private set; }
		public double TotalAverage { get; private set; }
        public ushort BestPing { get; private set; }
        public ushort WorstPing { get; private set; }

        public void StopMeasuring()
        {
            // Get final PPS results
            PingsPerSecond();

            m_StopMeasurements = true;
        }

		public double PingsPerSecond()
		{
			if (TotalPings == 0)
				return 0d;

            // Only update this if we're still accepting measurements
            if(!m_StopMeasurements)
                m_LastPingsPerSecond = TotalPings / (DateTime.UtcNow - m_StartTimer).TotalSeconds;

            return m_LastPingsPerSecond;
        }

		// Return the average of the current window
		public double GetRollingAverage()
		{
			// Return cached result if called multiple times w/ same TotalPings
			if (m_LastRollingAverageTotal == TotalPings)
				return m_LastRollingAverageResult;

			// Calculate and cache rolling average
			m_LastRollingAverageResult = 0d;

			for (var i = 0; i < m_IndicesUsed; i++)
				m_LastRollingAverageResult += m_RollingWindow[i] / (double)m_IndicesUsed;

			return m_LastRollingAverageResult;
		}
	}
}
