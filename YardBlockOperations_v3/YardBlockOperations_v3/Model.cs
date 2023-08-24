using ILOG.Concert;
using ILOG.CPLEX;
using O2DESNet;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;

namespace YardBlockOperations_v3
{

    class Model : O2DESNet.Sandbox
    {
        // State variables
        int xyc, yyc; // position of the yard crane
        int s; // number of available yard cranes
        double Uc, tau_idle; // KPIs monitoring utilization
        DateTime t_idle_0; // KPI monitoring utilization
        List<Container> Q; // list of containers waiting to be handled
        public List<List<Container>> YB; // list of stacks of containers in the yard block

        // Imported classes
        Travel travel; // contains travel model for travel durations computations
        Job_creation job_creation; // contains job creation methods
        Schedule schedule; // contains scheduling algorithms
        Storage_allocation storage_allocation; // contains storage allocation algorithms
        int n; // number of vessels, inherited from job_creation class

        // Parameters
        static int X, Y, Z; // dimensions of the YB
        double[] Tv; // vessel's arrival rate
        double Ta; // container's arrival rate
        double Td; // container's departure rate

        // Global variables
        List<List<Container>> adjusted_YB; // to keep track of the future contents of the YB (accounting for queueing containers)
        Dictionary<int, List<(int, int)>> group_slot_dict; // to keep track of container group indices stored in the YB
        public List<Container> DiscardedContainers; // to keep track of containers discarded because of space issues (seldom happens)

        // KPIs
        public List<Container> ArchivedContainers; // to keep track of containers that went through the system already
        public List<double> UtilizationHistory; // to keep track of the utilization KPI history
        public List<List<List<Container>>> YardBlockHistory; // to keep track of history of contents of the YB
        public HourCounter HourCounter_YB_contents; // to keep track of the space utilization in the YB
        public int a_c; // to keep track of the number of container arrivals
        public int a_j; // to keep track of the number of job arrivals

        public Model(int X, int Y, int Z, double[] Tv, double Ta, double Td, List<List<Container>> YB0, Travel travel, Job_creation job_creation, int seed = 0) : base(seed)
        {
            // Initializing parameters
            Model.X = X;
            Model.Y = Y;
            Model.Z = Z;
            this.Tv= Tv;
            this.Ta = Ta;
            this.Td = Td;
            this.YB = YB0.ConvertAll(list => new List<Container>(list));

            // Importing objects from other classes
            this.travel = travel;
            this.job_creation = job_creation;
            this.schedule = new Schedule(travel);
            this.storage_allocation = new Storage_allocation(DefaultRS, job_creation.group_indices_import, job_creation.group_indices_export, job_creation.slots_ranges, X, Y, Z, travel);
            this.n = job_creation.n;

            // Initializing state variables
            xyc = 0;
            yyc = 0;
            s = 0;
            Q = new List<Container>();

            // Initializing KPIs
            t_idle_0 = ClockTime;
            tau_idle = 0;
            Uc = 1;

            // Initializing global variables
            adjusted_YB = YB.ConvertAll(list => new List<Container>(list)); // Deep copy of YB
            group_slot_dict = update_group_slot_dict(adjusted_YB);
            DiscardedContainers = new List<Container>();

            // Initializing history-related KPIs
            ArchivedContainers = new List<Container>();
            UtilizationHistory = new List<double>();
            YardBlockHistory = new List<List<List<Container>>> { YB.ConvertAll(list => new List<Container>(list)) };
            HourCounter_YB_contents = AddHourCounter();
            for (int i = 0; i < YB.Count; i++) // Counter must be initialized based on the contents of YB0
            {
                for (int j = 0; j < YB[i].Count; j++)
                {
                    HourCounter_YB_contents.ObserveChange(1);
                }
            }
            a_c = 0; a_j = 0;

            // Scheduling first vessel and container arrivals
            Schedule(() => Vessel_arrival(0), TimeSpan.FromSeconds(1)); // first vessel is scheduled within 1 second to avoid null division when computing Uc later
            Schedule(() => Container_arrival(), TimeSpan.FromHours(O2DESNet.Distributions.Exponential.Sample(DefaultRS, Ta)));
            Schedule(() => Container_departure(), TimeSpan.FromHours(O2DESNet.Distributions.Exponential.Sample(DefaultRS, Td)));
        }




        #region Events - Job arrivals

        void Vessel_arrival(int v)
        {
            // Triggering Job_arrival
            Schedule(() => Job_arrival(v));

            // Scheduling next arrival
            int v_prime = (v + 1) % n;
            double T_v = Tv[v];
            Schedule(() => Vessel_arrival(v_prime), TimeSpan.FromHours(T_v));
        }

        void Container_arrival()
        {
            // Triggering Job_arrival
            Schedule(() => Job_arrival(n + 1)); // parameter n+1 is set to correspond to container arrival in the Job_creation.Create_container_list method

            // Scheduling next arrival
            Schedule(() => Container_arrival(), TimeSpan.FromHours(O2DESNet.Distributions.Exponential.Sample(DefaultRS, Ta)));
        }

        void Container_departure()
        {
            // Triggering Job_arrival
            Schedule(() => Job_arrival(n + 2)); // parameter n+2 is set to correspond to container departure in the Job_creation.Create_container_list method

            // Scheduling next departure
            Schedule(() => Container_departure(), TimeSpan.FromHours(O2DESNet.Distributions.Exponential.Sample(DefaultRS, Td)));
        }

        void Job_arrival(int v)
        {
            // Update projection of the system
            adjusted_YB = update_adjusted_YB(YB, Q);
            group_slot_dict = update_group_slot_dict(adjusted_YB);

            // Creating a list of containers to be handled and adding it to the queue
            List<Container> new_job = job_creation.Create_container_list(vessel_index: v, YB: adjusted_YB, groups_in_YB: group_slot_dict, model_time: ClockTime, model_RS: DefaultRS);
            foreach (Container container in new_job)
            {
                Q.Add(container);
            }

            // Allocate storage space to the containers of the queue
            for (int container_index = 0; container_index < Q.Count; container_index++)
            {
                Container container = Q[container_index];
                //(container.x, container.y) = f_allocation(container, Q, YB);
                (container.x, container.y) = storage_allocation.f_allocation(container, adjusted_YB, group_slot_dict, strategy: "Random");
                adjusted_YB = storage_allocation.update_adjusted_YB();
                group_slot_dict = storage_allocation.update_group_slot_dict();
                if (container.x < 0)
                {
                    DiscardedContainers.Add(container); // if there is no room for a container, it is deleted from the system and stored in a list
                }
            }
            foreach (Container container in DiscardedContainers)
            {
                Q.Remove(container); // discarded containers are removed from the queue (seldom happens)
            }

            // Perform scheduling of the containers in the queue
            Q = schedule.bubblesort(Q);

            // Trigger Start_restoring event
            if (s == 0 & Q.Count > 0)
            {
                Schedule(() => Start_restoring(Q.First()));
            }

            // KPIs
            a_j++;
            a_c += new_job.Count;
        }
        #endregion



        #region Events - Yard crane operations

        void Start_restoring(Container i)
        {
            // Updating state variables: utilization and available servers
            tau_idle = tau_idle + (ClockTime - t_idle_0).TotalMinutes;
            Uc = 1 - tau_idle / (ClockTime - DateTime.MinValue).TotalMinutes;
            s += 1;

            // Compute YC restoring time
            int x = i.x;
            int y = i.y;
            if (i.SR.StartsWith("S")) // storage operation
            {
                // For storage ops, pick-up is in side_loading or end-loading zones 
                // (for retrieval ops, pick-up is at stack
                x = i.SR.EndsWith("L") ? i.x : 0;
                y = i.SR.EndsWith("L") ? 0 : i.y;
            }
            double t_restore = travel.Restore(x0: xyc, y0: yyc, x1: x, y1: y);

            // Scheduling next event based on operation type
            if (i.SR.StartsWith('S'))
            {
                Schedule(() => Pickup_at_vehicle(i), TimeSpan.FromSeconds(t_restore));
            }
            else
            {
                Schedule(() => Pickup_at_unstack(i), TimeSpan.FromSeconds(t_restore));
            }
        }

        void Pickup_at_vehicle(Container i)
        {
            // Updating state variables: YC position and queue contents
            (xyc, yyc) = f_position(i.x, i.y, i.SR);
            Q.Remove(i);

            // Computing traveling time to stack
            int stack_index = x_y_to_index(i.x, i.y);
            double z = YB[stack_index].Count * 2.395;    // ISO standard container height is 2.395m
            double t_stack = travel.Stack(x0: xyc, y0: yyc, z0: 0, x1: i.x, y1: i.y, z1: z);

            // Scheduling next event
            Schedule(() => Dropoff_at_stack(i), TimeSpan.FromSeconds(t_stack));

            // KPIs
            i.waiting_time = (ClockTime - i.t_in);
        }

        void Dropoff_at_stack(Container i)
        {
            // Updating state variables: YC position and YB contents
            (xyc, yyc) = (i.x, i.y);
            int stack_index = x_y_to_index(i.x, i.y);
            YB[stack_index].Add(i);

            // Changing state of import containers already ordered
            if (i.scheduled_retrieval != DateTime.MinValue) 
            {
                i.SR = "RL";
                i.T = i.scheduled_retrieval;
            }

            // Scheduling next event
            Schedule(() => Start_idling());

            // KPIs
            i.t_YB_in = ClockTime;
            HourCounter_YB_contents.ObserveChange(1);
        }

        void Pickup_at_unstack(Container i)
        {
            // Updating state variables: YC position, YB and queue contents
            (xyc, yyc) = (i.x, i.y);
            int stack_index = x_y_to_index(i.x, i.y);
            YB[stack_index].Remove(i);
            Q.Remove(i);

            // Computing traveling time to drop-off
            int x = i.SR.EndsWith("L") ? i.x : 0; // landside
            int y = i.SR.EndsWith("L") ? 0 : i.y;
            double z = YB[stack_index].Count * 2.395;    // ISO standard container height is 2.395m
            double t_stack = travel.Stack(x0: xyc, y0: yyc, z0: z, x1: x, y1: y, z1: 0);

            // Scheduling next event
            Schedule(() => Dropoff_at_vehicle(i), TimeSpan.FromSeconds(t_stack));

            // KPIs
            i.t_YB_out = ClockTime;
            i.dwelling_time = i.t_YB_out - i.t_YB_in;
            HourCounter_YB_contents.ObserveChange(-1);
        }

        void Dropoff_at_vehicle(Container i)
        {
            // Updating state variable: YC position
            (xyc, yyc) = f_position(i.x, i.y, i.SR);

            // Scheduling next event
            Schedule(() => Start_idling());

            // KPIs
            i.t_out = ClockTime;
            i.lead_time = i.t_out - i.t_in;
            ArchivedContainers.Add(i);
        }

        void Start_idling()
        {
            // Updating state variables: idling time, queue sequence and available servers
            t_idle_0 = ClockTime;
            //Q = f_schedule(Q);
            s -= 1;

            // Trigger next event if queue is not empty
            if (Q.Count > 0)
            {
                Schedule(() => Start_restoring(Q.First()));
            }

            // KPIs
            UtilizationHistory.Add(Uc);
            YardBlockHistory.Add(YB.ConvertAll(list => new List<Container>(list)));
        }
        #endregion



        #region Functions

        (int, int) f_position(int xi, int yi, string SRi)
        {
            if (SRi.EndsWith('L'))
            {
                return (xi, 0);
            }
            else
            {
                return (0, yi);
            }
        }

        public static int x_y_to_index(int x, int y)
        {
            // ground slots are numbered from 0 to X*Y-1, increasing x first and then y
            return (x - 1) + (y - 1) * X;
        }

        public static (int x, int y) index_to_x_y(int index, int X)
        {
            return (index % X + 1, index / X + 1);
        }

        List<List<Container>> update_adjusted_YB(List<List<Container>> YB, List<Container> Q)
        {
            List<List<Container>> adjusted_YB = YB.ConvertAll(list => new List<Container>(list)); // deep copy of the YB
            foreach (Container container in Q)
            {
                if (container.x > 0)
                {
                    int slot = x_y_to_index(container.x, container.y);

                    if (container.SR.StartsWith('R'))
                    {
                        adjusted_YB[slot].Remove(container);
                    }
                    else
                    {
                        adjusted_YB[slot].Add(container);
                    }
                }
            }
            return adjusted_YB;
        }

        static Dictionary<int, List<(int, int)>> update_group_slot_dict(List<List<Container>> YB) // Associates the group of container to its slots in YB
        {
            Dictionary<int, List<(int, int)>> group_slot_dict = new Dictionary<int, List<(int, int)>>();

            for (int x = 1; x < X + 1; x++)
            {
                for (int y = 1; y < Y + 1; y++)
                {
                    int slot_index = x_y_to_index(x, y);
                    List<Container> slot = YB[slot_index];
                    if (slot.Count > 0)
                    {
                        Container dummy = slot[0];
                        int gp_id = dummy.group;
                        if (group_slot_dict.Keys.Contains(gp_id))
                        {
                            group_slot_dict[gp_id].Add((x, y));
                        }
                        else
                        {
                            group_slot_dict[gp_id] = new List<(int, int)>();
                            group_slot_dict[gp_id].Add((x, y));
                        }
                    }
                }
            }
            return group_slot_dict;
        }
        #endregion

        protected override void WarmedUpHandler()
        {
            base.WarmedUpHandler();
            ArchivedContainers.Clear();
            UtilizationHistory.Clear();
        }
    }

}
