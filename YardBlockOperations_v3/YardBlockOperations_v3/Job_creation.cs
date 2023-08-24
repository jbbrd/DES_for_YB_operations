using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace YardBlockOperations_v3
{
    class Job_creation : O2DESNet.Sandbox // change the structure: no subjobs, no point in complicating things. Also, problem of consistent 0 units in the jobs after 1 job gets 0 units.
    {
        public int n; // number of vessels
        int average_import_containers; // parameter for the poisson distribution of the number of import containers
        double transshipment_proportion; // proportion of cargo volume (excluding import containers) of transshipment type

        public List<int>[] group_indices_import; // list of group indices for import containers for each vessel
        public List<int>[] group_indices_export; // list of group indices for export containers for each vessel
        public List<int>[] slots_ranges; // list of YB slots assigned to each vessel


        public Job_creation(int n, int average_import_containers, double transshipment_proportion, List<int>[] group_indices_import, List<int>[] group_indices_export, List<int>[] slots_ranges)
        {
            this.n = n;
            this.average_import_containers = average_import_containers;
            this.transshipment_proportion = transshipment_proportion;
            this.group_indices_import = group_indices_import;
            this.group_indices_export = group_indices_export;
            this.slots_ranges = slots_ranges;
        }

        public List<Container> Create_container_list (int vessel_index, List<List<Container>> YB, Dictionary<int, List<(int, int)>> groups_in_YB, DateTime model_time, Random model_RS)
        {
            // The job is considered scheduled is the vessel index is within the range of allowable indices
            // If the vessel index is n+1, the job is a single container
            bool scheduled = vessel_index < n;


            List<Container> container_list = new List<Container>();

            if (scheduled) // Scheduled job creation (unloading and loading of a vessel)
            {
                // Unloading of import containers
                int nb_of_containers = O2DESNet.Distributions.Poisson.Sample(model_RS, average_import_containers);

                for (int c = 1; c <= nb_of_containers; c++) // iterating to create individual containers
                {
                    // Choosing group within the range of indices of the vessel
                    int index = model_RS.Next(group_indices_import[vessel_index].Count);
                    int group = group_indices_import[vessel_index][index];
                    // Creating container and adding it to the list
                    Container container = new Container(x: 0, y: 0, group: group, T: model_time, SR: "SS", type: "import", current_time: model_time);
                    container_list.Add(container);
                }

                // Loading of export containers
                for (int slot = slots_ranges[vessel_index][0]; slot <= slots_ranges[vessel_index][^1]; slot++)
                {
                    // Going through the YB, we add every container of every slot to the container list
                    foreach (Container container in YB[slot])
                    {
                        container.SR = "RS";
                        container.T = model_time;
                        container_list.Add(container);
                    }
                }
            }
            else // Unscheduled job creation: random retrieval or storage of single container
            {
                if (vessel_index == n + 1) // export container arrival
                {
                    // Choosing a destination vessel for the container
                    vessel_index = model_RS.Next(n); // vessel_index was a dummy before, it is now set to a specific value corresponding to a vessel

                    // Choosing group within the range of indices of the vessel
                    int index = model_RS.Next(group_indices_export[vessel_index].Count);
                    int group = group_indices_export[vessel_index][index];

                    // Choosing origin of container (export or transshipment type)
                    string op_type = model_RS.NextDouble() <= transshipment_proportion ? "SS" : "SL"; // SS is transshipment, SL is export
                    
                    // Creating container and adding it to the list
                    Container container = new Container(x: 0, y: 0, group: group, T: model_time, SR: op_type, type: "export", current_time: model_time);
                    container_list.Add(container);
                }
                else // import container departure
                {
                    // Choosing an origin vessel for the container
                    vessel_index = model_RS.Next(n); // vessel_index was a dummy before, it is now set to a specific value corresponding to a vessel

                    // Choosing group within the available groups in the YB
                    List<int> available_groups = groups_in_YB.Keys.Intersect(group_indices_import[vessel_index]).ToList(); // list of available groups for this vessel: intersection between group indices list and groups in YB
                    if (available_groups.Count > 0) // If there is at least one container in the vessel's sub-block
                    {
                        // Choosing a random group among available ones
                        int index = model_RS.Next(available_groups.Count);
                        int group = available_groups[index];

                        // Identify the stacking location of this group of containers
                        int nb_of_stacks = groups_in_YB[group].Count;
                        int stack_index = model_RS.Next(nb_of_stacks);
                        (int, int) stack_coordinates = groups_in_YB[group][stack_index];
                        int slot_index = Model.x_y_to_index(stack_coordinates.Item1, stack_coordinates.Item2);

                        // Retrieving last container of the stack and adding it to the list
                        Container container = YB[slot_index][^1];
                        if (container.t_YB_in > DateTime.MinValue)
                        {
                            container.SR = "RL";
                            container.T = model_time;
                        }
                        else
                        {
                            container.scheduled_retrieval = model_time; // we cannot directly modify container.SR as the container might still be in the queue
                        }
                        
                        container_list.Add(container);
                    }
                }
            }
            return container_list;
        }
    }
}
