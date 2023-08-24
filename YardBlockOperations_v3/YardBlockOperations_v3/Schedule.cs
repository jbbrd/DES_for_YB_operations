using ILOG.Concert;
using ILOG.CPLEX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YardBlockOperations_v3
{
    class Schedule
    {
        Travel travel;

        public Schedule(Travel travel)
        {
            this.travel = travel;
        }

        public List<Container> bubblesort(List<Container> Q)
        {
            int n = Q.Count;
            int start = 0;
            bool swap = true;

            while (swap)
            {
                swap = false;
                for (int i = n - 1; i >= start + 1; i--)
                {
                    Container Ci = Q[i];
                    Container Cj = Q[i - 1];
                    if (Ci.T < Cj.T)
                    {
                        Q[i] = Cj;
                        Q[i - 1] = Ci;
                        swap = true;
                    }
                }
                start += 1;
            }
            return Q;
        }

        public List<Container> rolling_TSP(List<Container> Q, int rolling_horizon = 5)
        {
            List<Container> return_Q = new List<Container>();
            int start_range = 0;
            List<Container> Q_range = new List<Container>();
            List<Container> Q_sorted = new List<Container>();

            while (start_range + rolling_horizon < Q.Count)
            {
                Q_range = Q.GetRange(start_range, rolling_horizon);
                Q_sorted = TSP(Q_range);
                return_Q = return_Q.Concat(Q_sorted).ToList();
                start_range += rolling_horizon;
            }
            if (Q.Count % rolling_horizon > 0)
            {
                Q_range = Q.GetRange(start_range, Q.Count % rolling_horizon);
                Q_sorted = TSP(Q_range);
                return_Q = return_Q.Concat(Q_sorted).ToList();
            }
            return return_Q;

        }


        public List<Container> TSP(List<Container> Q)
        {
            try
            {
                List<Container> return_Q = new List<Container>();

                // Create CPLEX object
                Cplex cplex = new Cplex();

                // Problem data (city coordinates)
                int numCities = Q.Count * 2;
                int[,] cityCoordinates = new int[numCities, 2];

                int cityIndex = 0;
                foreach (Container container in Q)
                {
                    if (container.SR.StartsWith("S"))
                    {
                        if (container.SR.EndsWith("L"))
                        {
                            cityCoordinates[cityIndex, 0] = container.x;
                            cityCoordinates[cityIndex, 1] = 0;
                        }
                        else
                        {
                            cityCoordinates[cityIndex, 0] = 0;
                            cityCoordinates[cityIndex, 1] = container.y;
                        }
                        cityIndex++;
                        cityCoordinates[cityIndex, 0] = container.x;
                        cityCoordinates[cityIndex, 1] = container.y;
                        cityIndex++;
                    }
                    else
                    {
                        cityCoordinates[cityIndex, 0] = container.x;
                        cityCoordinates[cityIndex, 1] = container.y;
                        cityIndex++;
                        if (container.SR.EndsWith("L"))
                        {
                            cityCoordinates[cityIndex, 0] = container.x;
                            cityCoordinates[cityIndex, 1] = 0;
                        }
                        else
                        {
                            cityCoordinates[cityIndex, 0] = 0;
                            cityCoordinates[cityIndex, 1] = container.y;
                        }
                        cityIndex++;
                    }
                }

                // Computing distance matrix
                double[,] costMatrix = new double[numCities, numCities];
                for (int i = 0; i < numCities; i++)
                {
                    for (int j = 0; j < numCities; j++)
                    {
                        int xi = cityCoordinates[i, 0];
                        int yi = cityCoordinates[i, 1];
                        int xj = cityCoordinates[j, 0];
                        int yj = cityCoordinates[j, 1];
                        costMatrix[i, j] = travel.Restore(xi, yi, xj, yj);
                    }
                }

                // Decision variables
                INumVar[,] x = new INumVar[numCities, numCities];
                for (int i = 0; i < numCities; i++)
                {
                    for (int j = 0; j < numCities; j++)
                    {
                        x[i, j] = cplex.BoolVar("x[" + i + "," + j + "]");
                    }
                }

                INumVar[] u = new INumVar[numCities];
                for (int i = 0; i < numCities; i++)
                {
                    u[i] = cplex.NumVar(0, double.MaxValue, "u[" + i + "]");
                }

                // Objective : minimize total travel time
                ILinearNumExpr objective = cplex.LinearNumExpr();
                for (int i = 0; i < numCities; i++)
                {
                    for (int j = 0; j < numCities; j++)
                    {
                        objective.AddTerm(costMatrix[i, j], x[i, j]);
                    }
                }
                cplex.AddMinimize(objective);

                // Constraint: each city need to be visited once
                for (int i = 0; i < numCities; i++)
                {
                    ILinearNumExpr constraint = cplex.LinearNumExpr();
                    for (int j = 0; j < numCities; j++)
                    {
                        if (i != j)
                            constraint.AddTerm(1.0, x[i, j]);
                    }
                    cplex.AddEq(constraint, 1);
                }

                // Constraint: each city need to be left once
                for (int j = 0; j < numCities; j++)
                {
                    ILinearNumExpr constraint = cplex.LinearNumExpr();
                    for (int i = 0; i < numCities; i++)
                    {
                        if (i != j)
                            constraint.AddTerm(1.0, x[i, j]);
                    }
                    cplex.AddEq(constraint, 1);
                }

                // Constraint: avoid subtours
                for (int i = 1; i < numCities; i++)
                {
                    for (int j = 2; j < numCities; j++)
                    {
                        if (i != j)
                        {
                            ILinearNumExpr subTourConstraint = cplex.LinearNumExpr();
                            subTourConstraint.AddTerm(1.0, u[i]);
                            subTourConstraint.AddTerm(-1.0, u[j]);
                            subTourConstraint.AddTerm(+numCities, x[i, j]);
                            cplex.AddLe(subTourConstraint, numCities - 1);
                        }
                    }
                    ILinearNumExpr subTourConstraint2 = cplex.LinearNumExpr();
                    subTourConstraint2.AddTerm(1.0, u[i]);
                    cplex.AddGe(subTourConstraint2, 0);
                }

                // Additional constraint: consecutive cities must be visited consecutively
                for (int i = 0; i < numCities - 1; i = i + 2) // indexes for every other city
                {
                    ILinearNumExpr constraint = cplex.LinearNumExpr();
                    constraint.AddTerm(1.0, x[i, i + 1]);
                    cplex.AddEq(constraint, 1);
                }

                // Solving
                cplex.Solve();

                // Optimal path
                if (cplex.GetStatus() == Cplex.Status.Optimal)
                {
                    int thisCity = 0;
                    List<int> optimalPath = new List<int> { thisCity };

                    for (int i = 0; i < numCities - 1; i++)
                    {
                        int nextCity = 0;
                        while (cplex.GetValue(x[thisCity, nextCity]) <= 0.5)
                        {
                            nextCity++;
                        }
                        thisCity = nextCity;
                        optimalPath.Add(thisCity);
                    }

                    // Translating optimal city sequence into container sequence
                    foreach (int city in optimalPath)
                    {
                        Container container = Q[city / 2];
                        if (!return_Q.Contains(container))
                        {
                            return_Q.Add(container);
                        }
                    }
                }
                else
                {
                    // No optimal solution found
                    foreach (Container container in Q)
                    {
                        return_Q.Add(container);
                    }
                }

                // Close CPLEX
                cplex.End();

                return return_Q;
            }
            catch (ILOG.Concert.Exception e)
            {
                Console.WriteLine("Error : " + e.Message);
                return Q;
            }
        }
    }
}
