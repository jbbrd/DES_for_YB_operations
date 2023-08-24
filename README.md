# DES_for_YB_operations
This repository contains a Discrete-Event Simulation (DES) model of an automated container yard block. It is built using C# language and the O²DES framework. This is the source code that was used in my master's thesis on "Dynamic decision making in an automated container yard block via optimization-embedded discrete-event simulation"." 
***

## Project description

This project intends to provide a DES simulation model of an automated yard block. Such a system is equipped with an automated yard crane, which handles containers' storage and retrieval operations in a yard block. The aim of this project is to test-out different strategies to operate the yard crane. The objective is to minimize the overall utilization. To do so, two problems are adressed: the Container Storage Location Assignment problem, and the Yard Crane Job Scheduling problem. The first consists in finding the best location within the yard block to store a container until it is retrieved. The second consists in finding the optimal sequencing of storage and retrieval tasks to minimize the yard crane's utilization. Several strategies (from rule-based to math-programming-based) are tested to adress each problem in this work. More details can be found in the thesis mentioned above. 

The current version of the code is final and was used to produce the results presented in the thesis. Provided some modifications are made, this framework could be reused to deal with similar port logistics problems.

## Requirements

The code was built using Visual Studio and is intended to be executed on it. The NuGet package O2DESNet is used.

A few algorithms in this code rely on the solving of linear programming problems. To this end, IBM's CPLEX solver is used. An API is provided for C# in Visual Studio, however the solver needs to be pre-installed on the machine to run the code. A free academic license is available, the version of the software used for this work is: IBM ILOG CPLEX Optimization Studio V22.1.1 Multiplatform Multilingual eAssembly (part number G0798ML). More information on the licenses and pricing here: https://www.ibm.com/products/ilog-cplex-optimization-studio/pricing.

## Project structure and classes

### Main classes

* Model.cs - This class contains the actual discrete-event simulation model. Events are created and linked using the O²DES framework.
* Main.cs - This class contains the main execution of the algorithm. It sets the parameters of the model, and triggers the simulation to produce desired results (warm-up analysis, confidence intervals, ...). The results of the simulation are gathered in .csv files in the project's folder: project_name > bin > Debug > ***.csv

### Objects

* Container.cs - This class is used to build the container objects with several attributes.

### Functional classes

These classes are separated from the main classes to simplify the modeling and provide more flexibility in scenario design. To test a different scenario (different parameters or strategies), these classes alone need to be changed, and not the whole model.

#### Parameters  

* Job_cration.cs - This class is used to translate incoming requests into an actual container list for yard crane operations. In particular, new containers are created in this class according to the scenario settings.
* Travel.cs - This class intergrates a physical model of the yard crane's traveling. It is used to compute its travel time based on several parameters: origin, destination, and yard crane settings.

#### Operational strategies

* Storage_allocation.cs - This class contains all the storage location assignment strategies tested-out in the model.
* Schedule.cs - This class contains all the job scheduling strategies tested-out in the model. Several methods of this class can require the use of CPLEX solver. The program can still be executed without CPLEX, but these methods will not be accessible.
