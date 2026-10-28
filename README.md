# PetroLogStudio

A C# desktop application for reading, conditioning, and plotting petrophysical well log data (LAS files) into industry-standard 3-track composite log plots.

## System Design Overview

I designed the application by separating it into two main projects to keep the code clean and easy to maintain:

1. PetroLogStudio (Core Engine)

- Custom LAS Parser: Built from scratch without third-party parsing libraries. It uses streaming line reading and ReadOnlySpan<char> to save memory. It supports both LAS 2.0 and LAS 3.0, handles whitespace or comma delimiters (DLM . Comma), and includes logic to detect and remove duplicated sections to prevent column misalignment.
- Data Conditioner: Checks and cleans data quality before plotting.
- Log Plotter: Generates industry-standard 3-track composite plots using ScottPlot.
- Batch Processor: Processes all files in a folder with fault isolation (if any file is corrupted, the application will not crash; it logs the issue to the report instead).

2. PetroLogStudio.UI (WPF Desktop App)

- User interface for choosing a well data folder, with a real-time progress bar.
- Includes a Well Overview summary card and a Diagnostic Report tab.

## Data Conditioning Decisions

From inspecting all 50 well files, I found various data issues. Here are the rules and decisions I implemented to clean the data:

1. Curve Aliasing
   Different wells use different column names. I created a dictionary covering standard curve names (DEPT, GR, RES, RHOB, NPHI) with a fallback search (heuristic) using tool descriptions.

2. Depth Normalization
   Some files have unsorted or alternating depth values. The program checks and sorts depth values from shallow to deep.

3. Null Value Handling
   Converts dummy null values (such as -999.25 or -9999) into null to prevent curves from dropping to zero or going off-scale.

4. Physical Data Sanitization

- Resistivity (RES): Removes values below 0.2 ohm.m or sensor flatlines to ensure safe plotting on a logarithmic scale.
- Bulk Density (RHOB): Natural rocks have densities between 1.0 and 2.95 g/cm³. Values outside this range—often caused by casing shoe artifacts or tool noise—are removed.
- Neutron Porosity (NPHI): Removes extreme negative values and tool error codes (such as -4223.1389) to keep values within a realistic geological range (-0.15 to 1.0).
- Isolated Spikes and Flatlines: Removes single-point spikes surrounded by null values so the logs remain clear and easy to interpret geologically.

## 3-Track Plot Display Standards

- Track 1 (Lithology): Gamma Ray curve (linear scale 0 - 150 GAPI) to separate sandstone and shale layers.
- Track 2 (Resistivity): Formation resistivity curve (logarithmic scale 0.2 - 2000 ohm.m) to identify hydrocarbon zones.
- Track 3 (Porosity / Fluid): Bulk Density (RHOB, red curve, 1.95 - 2.95 g/cm³) paired with Neutron Porosity (NPHI, blue curve, inverted scale 0.45 to -0.15) to identify gas crossover zones. If a well is missing one of these curves, the available curve is still plotted normally.

## Quick Start Guide

System Requirements: .NET 10.0 SDK installed on your computer.

Running the Desktop App (UI):

1. Open a terminal in the project directory and run:

   ```
   dotnet run --project src/PetroLogStudio.UI
   ```

2. Click the "Select LAS Folder" button to choose a folder with .las files (such as the TestData folder).
3. The program will process all files and generate plot images in the output/plots/ folder.
4. Click on any well name in the left panel to view its plot image or switch to the Diagnostic Report tab immediately.

## Known Limitations

1. Some wells only contain mud log gas data or only Gamma Ray without resistivity or density tools in the string. For these wells, Track 2 or Track 3 will only display the curves that are available.
2. While the system automatically detects and converts percentage units to decimals for Neutron Porosity, files with non-standard CWLS units may require additional unit conversion rules.

## How I Used AI

I used AI as a pair programming assistant in the following areas:

1. CWLS LAS 2.0 / 3.0 Specifications: Used AI to quickly summarize header sections and comma-delimited format details (DLM . Comma).
2. Petrophysical Standards: Consulted AI on industry-standard ranges for casing shoe artifacts and Density-Neutron crossplot scales.
3. Code Review & Debugging: Asked AI to check ScottPlot dual-axis compatibility and help investigate why certain files had column shifts. I fed raw data and logs to AI to analyze internal failure mechanisms, then used those insights to write code fixes myself (such as handling second ~P and ~A sections)
4. Speeding Up Learning: Used AI to understand concepts quickly—such as inverted depth axes, logarithmic resistivity scales, Density-Neutron crossover scales, ScottPlot, LINQ, Task Async, WPF, file streaming, and memory-efficient parsing. AI condensed these topics into practical takeaways, saving me days of reading geophysical documentation from scratch and acting as a force multiplier to turn my ideas into C# code.

Delivery Note on Plot Outputs:

- Primary deliverables according to specification are placed in output_plots/ (50 composite well logs).
- The core engine successfully verified and processed the entire test suite of 203 wells without unhandled exceptions. Full batch results and diagnostics are documented in batch_report.md.
