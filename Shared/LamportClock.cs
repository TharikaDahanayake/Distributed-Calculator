using System;

namespace Shared
{
    public class LamportClock
    {
        private int timestamp;

        public LamportClock()
        {
            timestamp = 0;
        }

        public int GetTime()
        {
            return timestamp;
        }

        public void Increment()
        {
            timestamp++;
        }

        // When receiving a message, update local clock to max(local, received) + 1
        public void UpdateOnReceive(int receivedTimestamp)
        {
            timestamp = Math.Max(timestamp, receivedTimestamp) + 1;
        }

        public override string ToString()
        {
            return timestamp.ToString();
        }
    }
}
