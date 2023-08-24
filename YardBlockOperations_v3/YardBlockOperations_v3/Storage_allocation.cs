using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace YardBlockOperations_v3
{
    internal class Storage_allocation
    {
        Random DefaultRS; // model's random stream

        List<int>[] group_indices_import; // list of group indices for import containers for each vessel
        List<int>[] group_indices_export; // list of group indices for export containers for each vessel
        List<int>[] slots_ranges; // list of YB slots assigned to each vessel

        int X, Y, Z; // yard block dimensions

        List<List<Container>> adjusted_YB; // local copy of model's variable
        Dictionary<int, List<(int, int)>> group_slot_dict; // local copy of model's variable
        Travel travel; // local copy of travel object


        public Storage_allocation(Random rndstrm, List<int>[] group_indices_import, List<int>[] group_indices_export, List<int>[] slots_ranges, int X, int Y, int Z, Travel travel)
        {
            DefaultRS = rndstrm;
            this.group_indices_import = group_indices_import;
            this.group_indices_export = group_indices_export;
            this.slots_ranges = slots_ranges;
            this.adjusted_YB = new List<List<Container>>();
            this.group_slot_dict = new Dictionary<int, List<(int, int)>>();
            this.X = X;
            this.Y = Y;
            this.Z = Z;
            this.travel = travel;
        }

        int find_vessel(Container container)
        {
            // Using group number to retrieve vessel number
            int vessel_index = 0;
            while (! (group_indices_export[vessel_index].Contains(container.group) | group_indices_import[vessel_index].Contains(container.group))){
                vessel_index++;
            }
            return vessel_index;
        }

        public (int, int) f_allocation(Container container, List<List<Container>> adjusted_YB, Dictionary<int, List<(int, int)>> group_slot_dict, string strategy = "Random")
        {
            // Update local copies of model's variables
            this.adjusted_YB = adjusted_YB;
            this. group_slot_dict = group_slot_dict;
            
            // Retrieving container assigned slot
            int xi = container.x; int yi = container.y;

            if (xi == 0 & yi == 0) // no slot assigned to the container
            {
                // Retrieve container group
                int group = container.group;

                // Checking if there is an empty stack of corresponding group
                if (group_slot_dict.Keys.Contains(group))
                {
                    foreach ((int, int) slot_coordinates in group_slot_dict[group])
                    {
                        // Going through each slot of the corresponding group
                        int x_slot = slot_coordinates.Item1;
                        int y_slot = slot_coordinates.Item2;
                        int index = Model.x_y_to_index(x_slot, y_slot);
                        int slot_contents = adjusted_YB[index].Count;
                        // Checking if there is remaining space in the stack
                        if (slot_contents < Z)
                        {
                            this.adjusted_YB[index].Add(container);
                            return slot_coordinates;
                        }
                    }
                }

                // In case no stack for this group has available space, slot is assigned based on selected strategy
                if (strategy == "Random")
                {
                    return f_allocation_random(container);
                }
                else if (strategy == "Increment")
                {
                    return f_allocation_incremental(container);
                }
                else if (strategy == "Decrement")
                {
                    return f_allocation_decremental(container);
                }
                else if (strategy == "Shortest")
                {
                    return f_allocation_shortest_to_destination(container);
                }
                else
                {
                    return (-1, -1);
                }
            }
            else
            {
                return (xi, yi);
            }
        }

        (int, int) f_allocation_random(Container container)
        {
            // Retrieve vessel index to select suitable storage bays
            int vessel_index = find_vessel(container);
            int bay_range_start = 1;
            for (int i = 0; i < vessel_index; i++)
            {
                bay_range_start += slots_ranges[i].Count / X;
            }
            int bay_range_end = bay_range_start + slots_ranges[vessel_index].Count / X - 1;

            // Generate random coordinates in allowable range
            int x = DefaultRS.Next(1, X + 1);
            int y = DefaultRS.Next(bay_range_start, bay_range_end + 1);
            
            // Retrieve container characteristics
            int group = container.group;
            int index = Model.x_y_to_index(x, y);

            // Keep generating coordinates until suitable space is found or too much tries have been made
            int break_counter = 0; // need to add a break counter in case there is no available slot in the yard block
            while (adjusted_YB[index].Count > 0)
            {
                x = DefaultRS.Next(1, X + 1);
                y = DefaultRS.Next(bay_range_start, bay_range_end + 1);
                index = Model.x_y_to_index(x, y);
                break_counter++;
                if (break_counter > 200)
                {
                    return (-1, -1);
                }
            }

            // Update model's variables
            adjusted_YB[index].Add(container);
            if (group_slot_dict.Keys.Contains(group))
            {
                group_slot_dict[group].Add((x, y));
            }
            else
            {
                group_slot_dict[group] = new List<(int, int)> { (x, y) };
            }

            // Return coordinates
            return (x, y);
        }

        (int, int) f_allocation_incremental(Container container)
        {
            // Retrieve vessel index to select suitable storage bays
            int vessel_index = find_vessel(container);
            int bay_range_start = 1;
            for (int i = 0; i < vessel_index; i++)
            {
                bay_range_start += slots_ranges[i].Count / X - 1;
            }
            int bay_range_end = bay_range_start + slots_ranges[vessel_index].Count / X - 1;

            // Assign first slot coordinates
            int x = 1;
            int y = bay_range_start;

            // Retrieve container's characteristics
            int group = container.group;
            int index = Model.x_y_to_index(x, y);

            // Re-assign coordinates by incrementing until suitable slot is found
            while (adjusted_YB[index].Count > 0)
            {
                index++;
                if (index > X * bay_range_end) // condition in case the whole sub-block has been gone through
                {
                    return (-1, -1);
                }
            }
            x = index % X + 1;
            y = index / X + 1;

            // Update model's variables
            adjusted_YB[index].Add(container);
            if (group_slot_dict.Keys.Contains(group))
            {
                group_slot_dict[group].Add((x, y));
            }
            else
            {
                group_slot_dict[group] = new List<(int, int)> { (x, y) };
            }

            // Return coordinates
            return (x, y);
        }

        (int, int) f_allocation_decremental(Container container)
        {
            // Retrieve vessel index to select suitable storage bays
            int vessel_index = find_vessel(container);
            int bay_range_start = 1;
            for (int i = 0; i < vessel_index; i++)
            {
                bay_range_start += slots_ranges[i].Count / X - 1;
            }
            int bay_range_end = bay_range_start + slots_ranges[vessel_index].Count / X - 1;

            // Assign last slot coordinates
            int x = X;
            int y = bay_range_end;

            // Retrieve container's characteristics
            int group = container.group;
            int index = Model.x_y_to_index(x, y);

            // Re-assign coordinates by decrementing until suitable slot is found
            while (adjusted_YB[index].Count > 0)
            {
                index--;
                if (index < X * (bay_range_start - 1))  // condition in case the whole sub-block has been gone through
                {
                    return (-1, -1);
                }
            }
            x = index % X + 1;
            y = index / X + 1;

            // Update model's variables
            adjusted_YB[index].Add(container);
            if (group_slot_dict.Keys.Contains(group))
            {
                group_slot_dict[group].Add((x, y));
            }
            else
            {
                group_slot_dict[group] = new List<(int, int)> { (x, y) };
            }

            // Return coordinates
            return (x, y);
        }

        (int, int) f_allocation_shortest_to_destination(Container container)
        {
            // Retrieve vessel index to select suitable storage bays
            int vessel_index = find_vessel(container);
            int bay_range_start = 1;
            for (int i = 0; i < vessel_index; i++)
            {
                bay_range_start += slots_ranges[i].Count / X - 1;
            }
            int bay_range_end = bay_range_start + slots_ranges[vessel_index].Count / X - 1;

            // Assign reference point for distance computation
            int x_ref = 0;
            int y_ref = container.type == "import" ? bay_range_start : bay_range_end;

            // Compute distance matrix from reference point
            double[] slot_distances = slot_distances_from_ref(y_ref, x_ref);

            // Initialize dummy variables to find smallest distance
            double min_dist = double.PositiveInfinity;
            int min_dist_index = 0;

            // Go through distance matrix to find the empty slot closest to the reference point
            int min_index = Model.x_y_to_index(1, bay_range_start);
            int max_index = Model.x_y_to_index(X, bay_range_end);
            for (int index = min_index; index <= max_index; index ++)
            {
                int slot_count = adjusted_YB[index].Count;
                double distance_from_ref = slot_distances[index];
                if (slot_count == 0 & distance_from_ref < min_dist)
                {
                    min_dist = distance_from_ref;
                    min_dist_index = index;
                }
            }

            // If the dummy variables are not updated, no closest point was found
            if(min_dist == double.PositiveInfinity)
            {
                return (-1, -1);
            }
            else
            {
                // Update model's variables
                int group = container.group;
                int x = 0, y = 0;
                (x, y) = Model.index_to_x_y(min_dist_index, X);

                adjusted_YB[min_dist_index].Add(container);
                if (group_slot_dict.Keys.Contains(group))
                {
                    group_slot_dict[group].Add((x, y));
                }
                else
                {
                    group_slot_dict[group] = new List<(int, int)> { (x, y) };
                }

                return (x, y);
            }
        }

        double[] slot_distances_from_ref(int y_ref, int x_ref = 0) // used to compute travelling time from a reference point to any slot in the YB
        {
            double[] slot_distances = new double[X * Y];
            int x = 0;
            int y = 0;

            for (int index = 0; index < adjusted_YB.Count; index++) 
            {
                (x, y) = Model.index_to_x_y(index, X);
                slot_distances[index] = travel.Restore(x_ref, y_ref, x, y);
            }
            return slot_distances;
        }

        public List<List<Container>> update_adjusted_YB() // used to update model's variable
        {
            return adjusted_YB;
        }

        public Dictionary<int, List<(int, int)>> update_group_slot_dict() // used to update model's variable
        {
            return group_slot_dict;
        }
    }
}
