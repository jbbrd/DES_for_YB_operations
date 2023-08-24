using MathNet.Numerics.Distributions;
using MathNet.Numerics.Integration;
using MathNet.Numerics.Random;
using MathNet.Numerics.Statistics;
using O2DESNet;
using O2DESNet.Distributions;
using O2DESNet.RandomVariables.Continuous;
using System;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using YardBlockOperations_v3;
using static System.Net.Mime.MediaTypeNames;

#region Parameters

// Dimensions of the YB
int X = 8, Y = 40, Z = 4;

// Yard crane acceleration and speed parameters
double ax = 0.5, ay =1, az =0.5, vx = 1, vy = 4, vz =0.8, h = 13;

// Arrival and departure rates
double Ta = 0.15, Td = 1.5;

// Scenario parameters: number of groups, vessels, and vessels arrival schedule
int nb_of_groups = 200; // should be less than X*Y
int nb_of_vessels = 4;
double[] Tv = new double[] { 42, 42, 42, 42 }; // time delay between ship v and next ship

// Scenario parameters: average number of import containers (for Poisson distribution) and transshipment proportion (compared to export containers)
int average_import_containers = 22;
double transshipment_proportion = 0.9;

#endregion

#region Other parameters

// Initial conditions: YB contents
List<List<Container>> YB0 = new List<List<Container>>();
for (int i = 0; i < X * Y; i++)
{
    List<Container> list = new List<Container>();
    YB0.Add(list);
}

// YB clusters ranges
List<int>[] bay_ranges = new List<int>[nb_of_vessels];

int cluster_length = Y / nb_of_vessels; // Y must be a multiple of the number of vessels for symmetrical clustering

for (int ship_index = 0; ship_index < nb_of_vessels; ship_index++)
{
    // Each vessel is assigned a range of consecutive bays of length cluster_length
    int starting_bay = ship_index * cluster_length + 1;
    bay_ranges[ship_index] = Enumerable.Range(starting_bay, cluster_length).ToList();
}

//YB clusters slots ranges
List<int>[] slots_ranges = new List<int>[nb_of_vessels];

int slots_per_cluster = X * Y / nb_of_vessels;

for (int ship_index = 0; ship_index < nb_of_vessels; ship_index++)
{
    // Each vessel is assigned a range of slots based on the bays it is assigned
    int starting_slot = ship_index * slots_per_cluster;
    slots_ranges[ship_index] = Enumerable.Range(starting_slot, slots_per_cluster).ToList();
}

// Groups attributions for each cluster
List<int>[] group_indices_import = new List<int>[nb_of_vessels];
List<int>[] group_indices_export = new List<int>[nb_of_vessels];

double import_proportion = 0.1; // proportion of import containers compared to total cargo volume
int groups_per_cluster = nb_of_groups / nb_of_vessels; // the number of groups must be a multiple of the number of vessels

for (int ship_index = 0; ship_index < nb_of_vessels; ship_index++)
{
    // Each vessel is assigned a range of consecutive indices, the first indices are for import containers and the remaining are for export
    int starting_group_index = ship_index * groups_per_cluster + 1;
    int nb_of_import_groups = Convert.ToInt32(Math.Round(import_proportion * groups_per_cluster));
    int nb_of_export_groups = groups_per_cluster - nb_of_import_groups;
    // Each of the following arrays hold n lists of indices
    group_indices_import[ship_index] = Enumerable.Range(starting_group_index, nb_of_import_groups).ToList(); 
    group_indices_export[ship_index] = Enumerable.Range(starting_group_index + nb_of_import_groups, nb_of_export_groups).ToList();
}
#endregion

#region Importing objects from other classes
// Travel characteristics of the YC (speed, acceleration, height)
Travel travel = new Travel(ax, ay, az, vx, vy, vz, h);

// Job_creation parameters and importation
Job_creation job_creation = new Job_creation(nb_of_vessels, average_import_containers, transshipment_proportion, group_indices_import, group_indices_export,slots_ranges);
#endregion

#region Initializing non-empty YB
var init = new Model(X: X, Y: Y, Z: Z, Tv, Ta, Td, YB0, travel, job_creation);
init.Run(TimeSpan.FromHours(7 * 24));
YB0 = init.YB;
#endregion



#region Running the model - Batch analysis


List<double> Ta_list = new List<double> { 0.25, 0.20, 0.15 };
List<double> Td_list = new List<double> { 1.0, 1.5, 2.0 };
int nb_of_batches = 7*4*4;
double batch_length = 6; // batch duration in hours
int nb_of_replications = 25;
double[,] batch_results = new double[nb_of_batches, nb_of_replications];


using (StreamWriter br = new StreamWriter($"..\\Warm-up analysis.csv", false))
{
    string headers = "Td, Ta";
    for (int i = 1; i <= nb_of_batches; i++)
    {
        headers = string.Concat(headers, $", Batch {i}");
    }
    br.WriteLine(headers);
    br.Close();
} // Export to csv

// Replicate results for different values of Ta
foreach (double Td_test in Td_list)
{
    foreach (double Ta_test in Ta_list)
    {
        for (int replication = 0; replication < nb_of_replications; replication++)
        {
            var sim = new Model(X: X, Y: Y, Z: Z, Tv, Ta: Ta_test, Td: Td_test, YB0, travel, job_creation, seed: replication);

            for (int batch = 0; batch < nb_of_batches; batch++)
            {
                sim.WarmUp(TimeSpan.Zero); // no warm-up for this analysis
                sim.Run(TimeSpan.FromHours(batch_length));
                if (sim.UtilizationHistory.Count > 0) // sometimes there are no new values in the batch, we can take the value from previous batch or previous replication
                {
                    batch_results[batch, replication] = sim.UtilizationHistory.Average();
                    //batch_results[batch, replication] = sim.HourCounter_YB_contents.AverageCount / (X * Y * Z);
                }
                else
                {
                    if (batch == 0)
                    {
                        batch_results[batch, replication] = batch_results[batch, replication - 1];
                    }
                    else
                    {
                        batch_results[batch, replication] = batch_results[batch - 1, replication];
                    }
                }
            }
        }

        // Compute ensemble averages
        List<double> ensemble_averages = new List<double>();
        for (int batch = 0; batch < nb_of_batches; batch++)
        {
            double ensemble_average = 0;
            for (int replication = 0; replication < nb_of_replications; replication++)
            {
                ensemble_average += batch_results[batch, replication];
            }
            ensemble_average = ensemble_average / nb_of_replications;

            ensemble_averages.Add(ensemble_average);
        }

        using (StreamWriter br = new StreamWriter($"..\\Warm-up analysis.csv", true))
        {
            string values = $"{Td_test}, {Ta_test}";
            for (int i = 0; i < ensemble_averages.Count; i++)
            {
                values = string.Concat(values, $", {ensemble_averages[i]}");
            }

            br.WriteLine(values);
            br.Close();
        } // Export to csv
    }
}
#endregion


#region Running the model - Compute average utilization

nb_of_replications = 10;

double warmup_length = 2 * 24; // based on warmup analysis, warm-up should be 2 days
double simultation_length = 10 * 2 * 24; // simulation length should be 10 * warmup_length

using (StreamWriter br = new StreamWriter($"..\\Results - Replication averages.csv", false))
{
    string headers = "Replication, Average Utilization";

    br.WriteLine(headers);
    br.Close();
} // Export to csv

List<double> replication_average = new List<double>();

for (int replication = 0; replication < nb_of_replications; replication++) // same number of replications as before
{
    var sim = new Model(X: X, Y: Y, Z: Z, Tv, Ta, Td, YB0, travel, job_creation, seed: replication);

    sim.WarmUp(TimeSpan.FromHours(warmup_length));
    sim.Run(TimeSpan.FromHours(simultation_length));

    replication_average.Add(sim.UtilizationHistory.Average());

    using (StreamWriter br = new StreamWriter($"..\\Results - Replication averages.csv", true))
    {
        string values = $"{replication}, {sim.UtilizationHistory.Average()}";

        br.WriteLine(values);
        br.Close();
    } // Export to csv
}
#endregion

/*
#region Running the model - Compute average utilization and CI

nb_of_replications = 45; // for 0.05 precision on confidence intervals

double warmup_length = 2 * 24; // based on warmup analysis, warm-up should be 2 days
double simultation_length = 10 * 2 * 24; // simulation length should be 10*warmup_length

using (StreamWriter br = new StreamWriter($"..\\Confidence intervals.csv", false))
{
    string headers = "Ta, mean, std_deviation, CI_low, CI_high";

    br.WriteLine(headers);
    br.Close();
} // Export to csv

List<double> replication_average = new List<double>();

for (int replication = 0; replication < nb_of_replications; replication++) // same number of replications as before
{
    var sim = new Model(X: X, Y: Y, Z: Z, Tv, Ta, Td, YB0, travel, job_creation, seed: replication);

    sim.WarmUp(TimeSpan.FromHours(warmup_length));
    sim.Run(TimeSpan.FromHours(simultation_length));

    replication_average.Add(sim.UtilizationHistory.Average());
}

var t = Math.Abs(MathNet.Numerics.Distributions.StudentT.InvCDF(location: 0.0, scale: 1.0, freedom: nb_of_replications - 1, p: 0.05));
double mean = replication_average.Average();
double std_dev = replication_average.StandardDeviation();
double epsilon = 0.05;
double R = Math.Pow(t * std_dev / epsilon, 2); // used to determine number of replications given precision epsilon
double CI_low = mean - t * std_dev / Math.Sqrt(nb_of_replications);
double CI_high = mean + t * std_dev / Math.Sqrt(nb_of_replications);

using (StreamWriter br = new StreamWriter($"..\\Confidence intervals.csv", true))
{
    string values = $"{Ta}, {mean}, {std_dev}, {CI_low}, {CI_high}";

    br.WriteLine(values);
    br.Close();
} // Export to csv
#endregion*/
