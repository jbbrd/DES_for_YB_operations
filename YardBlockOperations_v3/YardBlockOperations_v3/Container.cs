using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YardBlockOperations_v3
{
    class Container
    {
        public int x, y, group; // slot coordinates and class index
        public DateTime T; // scheduled handling time
        public string SR; // operation type: first letter = Storage / Retrieval, second letter = Sea / Land
        public string type; // container type: import or export (transhipment ~ export)

        // KPIs
        public DateTime t_in, t_out, t_YB_in, t_YB_out;
        public TimeSpan waiting_time, dwelling_time, lead_time;

        // Additional parameter for import containers
        public DateTime scheduled_retrieval;

        public Container(int x, int y, int group, DateTime T, string SR, string type, DateTime current_time)
        {
            this.x = x;
            this.y = y;
            this.group = group;
            this.T = T;
            this.SR = SR;
            this.type = type;

            t_in = current_time;
            t_YB_in = DateTime.MinValue;

            this.scheduled_retrieval = DateTime.MinValue;
        }
    }
}
