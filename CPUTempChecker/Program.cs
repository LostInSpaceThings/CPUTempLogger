using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO; 
using LibreHardwareMonitor.Hardware;

public class CpuTemperatureMonitor
{
    private const int MonitoringDurationMinutes = 10;
    private const int SamplingIntervalSeconds = 20;

    private class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer)
        {
            computer.Traverse(this);
        }
        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (IHardware subHardware in hardware.SubHardware) subHardware.Traverse(this);
        }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }

    public static async Task Main(string[] args)
    {
        Console.WriteLine("--- CPU Temp Logger by LostImSpaceThings ---");
        Console.WriteLine($"Monitoring for {MonitoringDurationMinutes} minutes, sampling every {SamplingIntervalSeconds} seconds...");
        Console.WriteLine("Keep this running for 10 MINUTES! You will be notified at the end to save the output to a FILE");
        Console.WriteLine("---------------------------------------------------------------------------------------------------------");


        List<double> recordedTemperatures = new List<double>();

        var visitor = new UpdateVisitor();
        var computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = false,
            IsMemoryEnabled = false,
            IsMotherboardEnabled = false,
            IsControllerEnabled = false,
            IsNetworkEnabled = false,
            IsStorageEnabled = false
        };

        try
        {
            computer.Open();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nERROR: Failed to open hardware monitor. Details: {ex.Message}");
            Console.WriteLine("Exiting application.");
            return;
        }

        ISensor cpuTempSensor = null;
        List<ISensor> tempSensors = new List<ISensor>();
        Console.WriteLine("\n--- Diagnosing Available Sensors ---");
        computer.Accept(visitor);

        foreach (var hardware in computer.Hardware)
        {
            if (hardware.HardwareType == HardwareType.Cpu)
            {
                tempSensors.AddRange(hardware.Sensors.Where(s => s.SensorType == SensorType.Temperature));

                Console.WriteLine($"Found {tempSensors.Count} temperature sensors on {hardware.Name}:");
                foreach (var s in tempSensors)
                {
                    Console.WriteLine($"- {s.Name}: {s.Value:F2}°C (Type: {s.SensorType})");
                }

                cpuTempSensor = tempSensors.FirstOrDefault(s =>
                    (s.Name.Contains("Package") || s.Name.Contains("Average") || s.Name.Contains("Tdie")) && s.Value.HasValue && s.Value.Value > 0
                );

                if (cpuTempSensor == null)
                {
                    cpuTempSensor = tempSensors
                        .Where(s => s.Value.HasValue && s.Value.Value > 0)
                        .OrderByDescending(s => s.Value.Value)
                        .FirstOrDefault();
                }

                if (cpuTempSensor == null)
                {
                    cpuTempSensor = tempSensors.FirstOrDefault();
                }

                break;
            }
        }

        if (cpuTempSensor == null)
        {
            Console.WriteLine("\nERROR: Could not find a suitable CPU temperature sensor.");
            computer.Close();
            return;
        }

        Console.WriteLine($"\nSUCCESS: Monitoring will use sensor: {cpuTempSensor.Name} (Current Reading: {cpuTempSensor.Value:F2}°C)");

        int totalSeconds = MonitoringDurationMinutes * 60;
        int totalSamples = totalSeconds / SamplingIntervalSeconds;
        int intervalMilliseconds = SamplingIntervalSeconds * 1000;

        Console.WriteLine($"Starting monitoring. Target Samples: {totalSamples} over {MonitoringDurationMinutes} minutes.");

        for (int i = 0; i < totalSamples; i++)
        {
            computer.Accept(visitor);
            float? sensorValue = cpuTempSensor.Value;

            if (sensorValue.HasValue)
            {
                double currentTemp = Math.Round((double)sensorValue.Value, 2);
                if (currentTemp > 10.0)
                {
                    recordedTemperatures.Add(currentTemp);
                }

                //Console.WriteLine($"[Sample {i + 1}/{totalSamples}] Current Temp: {currentTemp:F2}°C (Sensor: {cpuTempSensor.Name})");
            }
            else
            {
                Console.WriteLine($"[Sample {i + 1}/{totalSamples}] Sensor value not available. Skipping this sample.");
            }

            if (i < totalSamples - 1)
            {
                await Task.Delay(intervalMilliseconds);
            }
        }

        computer.Close();

        Console.WriteLine("\n--- Monitoring Complete. Calculalacting if you're doomed or not.. ---");

        if (recordedTemperatures.Count == 0)
        {
            Console.WriteLine("No valid temperatures were recorded (or all were 10°C or below).");
            return;
        }

        double minTemp = recordedTemperatures.Min();
        double maxTemp = recordedTemperatures.Max();
        double medianTemp = CalculateMedian(recordedTemperatures);

        string summaryReport = BuildSummaryReport(minTemp, maxTemp, medianTemp, recordedTemperatures.Count);

        Console.WriteLine(summaryReport);

        await PromptAndSaveResults(summaryReport, recordedTemperatures);
    }

    private static string BuildSummaryReport(double min, double max, double median, int count)
    {
        return $@"
--- Final Results ---
Total Successful Samples: {count}
---------------------
Minimum Temperature: {min:F2}°C
Maximum Temperature: {max:F2}°C
Median Temperature: {median:F2}°C
---------------------";
    }

    private static async Task PromptAndSaveResults(string summary, List<double> rawData)
    {
        Console.WriteLine("\n--- File Output ---");
        Console.WriteLine("Please specify the full path and file name where to save:");

        string filePath = Console.ReadLine()?.Trim();

        if (string.IsNullOrWhiteSpace(filePath))
        {
            Console.WriteLine("File path not provided. Results will not be saved to a file.");
            return;
        }

        try
        {

            string fileContent = summary + "\n\n--- Raw Temperature Data (Sequential) ---\n";
            fileContent += string.Join(", ", rawData.Select(t => $"{t:F2}"));

            await File.WriteAllTextAsync(filePath, fileContent);
            Console.WriteLine($"\nSuccessfully saved results to: {filePath}");
        }
        catch (IOException ex)
        {
            Console.WriteLine($"\nERROR: Failed to write file due to a path or access issue.");
            Console.WriteLine($"Ensure the directory exists and you have write permissions. Details: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nAn unexpected error occurred while saving the file. Details: {ex.Message}");
        }
    }


    private static double CalculateMedian(List<double> temperatures)
    {
        List<double> sortedTemps = temperatures.OrderBy(t => t).ToList();
        int count = sortedTemps.Count;
        double median;

        if (count % 2 == 1)
        {
            median = sortedTemps[count / 2];
        }
        else
        {
       
            int middleIndex = count / 2;
            double value1 = sortedTemps[middleIndex - 1];
            double value2 = sortedTemps[middleIndex];
            median = (value1 + value2) / 2.0;
        }

        return median;
    }
}