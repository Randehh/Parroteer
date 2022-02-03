using System;
using System.Linq;

namespace Parroteer.Utilities {
	public class AverageTimeCalculator {

		private int m_Count = 0;
		private int m_Pointer = 0;
		private DateTime m_LastEntry;
		private TimeSpan[] m_Times;
		private bool m_HasDoneLoop = false;
		private bool m_FirstTimeSet = false;

		public AverageTimeCalculator(int count) {
			m_Count = count;
			m_Times = new TimeSpan[count];
		}

		public void PushNewTime(DateTime time) {
			if (!m_FirstTimeSet) {
				m_LastEntry = time;
				m_FirstTimeSet = true;
				return;
			}

			m_Times[m_Pointer] = time - m_LastEntry;

			m_Pointer++;
			if (m_Pointer >= m_Count) {
				m_Pointer = 0;
				m_HasDoneLoop = true;
			}

			m_LastEntry = time;
		}

		public string GetEstimate(int ticksLeft = 1) {
			if (!m_HasDoneLoop) {
				return "Calculating...";
			}

			double doubleAverageTicks = m_Times.Average(timeSpan => timeSpan.Ticks);
			double totalSeconds = TimeSpan.FromTicks(Convert.ToInt64(doubleAverageTicks)).TotalSeconds * ticksLeft;

			TimeSpan time = TimeSpan.FromSeconds(totalSeconds);
			return $"Estimated time left: {time:mm\\:ss} minutes";
		}
	}
}
