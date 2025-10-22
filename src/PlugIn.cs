//  Copyright 2016 North Carolina State University, Center for Geospatial Analytics & 
//  Forest Service Northern Research Station, Institute for Applied Ecosystem Studies
//  Authors:  Francesco Tonini, Brian R. Miranda, Chris Jones

using Landis.Core;
using Landis.Library.Metadata;
using Landis.SpatialModeling;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Drawing;
using Landis.Library.Cohorts;
using Landis.Library.AgeOnlyCohorts;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace Landis.Extension.BaseEDA
{
    ///<summary>
    /// A disturbance plug-in that simulates Pathogen Dispersal and Disease.
    /// </summary>

    public class PlugIn
        : ExtensionMain
    {
        public static readonly ExtensionType type = new ExtensionType("disturbance:eda");
        public static readonly string ExtensionName = "Base EDA";
        public static MetadataTable<EventsLog> EventLog;
        public static ExternalClimateData loadedClimateData;

        private string statusMapName; 
        private string mortMapNames;

        private IEnumerable<IAgent> manyAgentParameters;
        private static IInputParameters parameters;
        private static ICore modelCore;
        private bool reinitialized;
        private Dictionary<string, ISpecies> speciesNameToISpecies;
        private HashSet<ISpecies> hostSpecies;
        private HashSet<ISpecies> vulnerableSpecies;

        private const int MAX_IMAGE_SIZE = 16384;
        

        //---------------------------------------------------------------------

        public PlugIn()
            : base(ExtensionName, type)
        {
        }

        //---------------------------------------------------------------------

        public override void LoadParameters(string dataFile,
                                            ICore mCore)
        {
            modelCore = mCore;
            InputParameterParser.EcoregionsDataset = modelCore.Ecoregions;
            InputParameterParser parser = new InputParameterParser();
            parameters = Landis.Data.Load<IInputParameters>(dataFile, parser);
        }

        //---------------------------------------------------------------------

        public static ICore ModelCore
        {
            get
            {
                return modelCore;
            }
        }

         /// <summary>
        /// Initializes the extension with a data file.
        /// </summary>
        public override void Initialize()
        {
            reinitialized = false;

            //initialize metadata
            MetadataHandler.InitializeMetadata(parameters.Timestep,
               parameters.StatusMapNames,
               parameters.MortMapNames,
               parameters.LogFileName,
               parameters.ManyAgentParameters,
               ModelCore);

            //get input params map names
            Timestep = parameters.Timestep;
            statusMapName = parameters.StatusMapNames;
            mortMapNames = parameters.MortMapNames;

            speciesNameToISpecies = new Dictionary<string, ISpecies>(StringComparer.OrdinalIgnoreCase);
            hostSpecies = new HashSet<ISpecies>();
            vulnerableSpecies = new HashSet<ISpecies>();
            foreach (var species in ModelCore.Species)
                speciesNameToISpecies[species.Name] = species;

            //initialize site variables:
            int numAgents = parameters.ManyAgentParameters.Count();
            SiteVars.Initialize(modelCore, numAgents);

            //Dispersal probdisp = new Dispersal();
            manyAgentParameters = parameters.ManyAgentParameters;
            if (numAgents > 1)
                throw new ApplicationException("Only a single EDA agent is supported for this build; found more than one.");

            if (numAgents == 1)
            {
                IAgent agent = manyAgentParameters.First();
                HashSet<string> neg = new HashSet<string>(agent.NegSppList != null ? agent.NegSppList.Select(s => s.Name) : Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                hostSpecies.Clear();
                vulnerableSpecies.Clear();
                List<string> hostNames = new List<string>();
                List<string> vulnerableNames = new List<string>();
                foreach (ISpecies species in ModelCore.Species)
                {
                    ISppParameters spp = agent.SppParameters[species.Index];
                    if (spp == null) continue;
                    if (neg.Contains(species.Name)) continue;
                    if (spp.LowHostScore > 0 || spp.MediumHostScore > 0 || spp.HighHostScore > 0)
                    {
                        hostSpecies.Add(species);
                        hostNames.Add(species.Name);
                    }
                    if (spp.LowVulnHostMortProb > 0 || spp.MediumVulnHostMortProb > 0 || spp.HighVulnHostMortProb > 0)
                    {
                        vulnerableSpecies.Add(species);
                        vulnerableNames.Add(species.Name);
                    }
                }
                Log.Info(LogType.General, $"EDA agent {agent.AgentName} host species: {string.Join(", ", hostNames)}");
                Log.Info(LogType.General, $"EDA agent {agent.AgentName} mortality species: {string.Join(", ", vulnerableNames)}");
            }

            int agentIndex = 0;

            //Initialize non-library climate data
            loadedClimateData = ClimateData.ReadClimateData(manyAgentParameters);

            foreach (IAgent activeAgent in manyAgentParameters)
            {
                if (activeAgent == null)
                    ModelCore.UI.WriteLine("Agent Parameters NOT loading correctly.");

                //read initial infection map and initialize cell status for each agent
                EpidemicRegions.ReadMap(activeAgent.InitEpiMap, agentIndex);
                agentIndex++;

                //initialize and populate dictionary with dispersal probabilities for current agent
                //probdisp.Initialize(activeAgent);
                Dispersal.Initialize(activeAgent);

                //Initialize climate data to calculate historic average for normalization
                ClimateVariableDefinition.CalculateHistoricClimateVariables(activeAgent);
                var dims = ModelCore.Landscape.Dimensions;
                int landscapeX = dims.Columns;
                int landscapeSize = dims.Rows * dims.Columns;
                InfectionStateDetection(ModelCore.Landscape.ActiveSites, landscapeX, landscapeSize);
            }

        }

        public new void InitializePhase2() 
        {
                SiteVars.InitializeTimeOfLastDisturbances();
                reinitialized = true;
        }

        //---------------------------------------------------------------------
        ///<summary>
        /// Run the EDA extension at a particular timestep.
        ///</summary>
        public override void Run()
        {
            Log.Init();
            {
                var dims = ModelCore.Landscape.Dimensions;
                int landscapeX = dims.Columns;
                int landscapeSize = dims.Rows * dims.Columns;
                InfectionStateDetection(ModelCore.Landscape.ActiveSites, landscapeX, landscapeSize);
            }
            
            ModelCore.UI.WriteLine("   Processing landscape for EDA events ...");
            if(!reinitialized)
                InitializePhase2();

            int eventCount = 0;

            int agentIndex = 0;
            foreach(IAgent activeAgent in manyAgentParameters)
            {

                Epidemic.Initialize(activeAgent);

                if (activeAgent.DispersalType == DispersalType.STATIC)
                {
                    ModelCore.UI.WriteLine("   Simulating spread of epidemic...");
                    Epidemic currentEpic = Epidemic.Simulate(activeAgent, ModelCore.CurrentTime, agentIndex);
                    if (currentEpic != null)
                    {
                        LogEvent(ModelCore.CurrentTime, currentEpic, activeAgent);

                        //----- Write Infection Status maps (SUSCEPTIBLE (0), INFECTED (cryptic-non symptomatic) (1), DISEASED (symptomatic) (2) --------
                        string path = MapNames.ReplaceTemplateVars(statusMapName, activeAgent.AgentName, ModelCore.CurrentTime);
                        modelCore.UI.WriteLine("   Writing infection status map to {0} ...", path);
                        using (IOutputRaster<BytePixel> outputRaster = modelCore.CreateRaster<BytePixel>(path, modelCore.Landscape.Dimensions))
                        {
                            BytePixel pixel = outputRaster.BufferPixel;
                            foreach (Site site in ModelCore.Landscape.AllSites)
                            {
                                if (site.IsActive)
                                {                                     
                                    pixel.MapCode.Value = (byte)(SiteVars.InfStatus[site][agentIndex] + 1);
                                }
                                else
                                {
                                    //Inactive site
                                    pixel.MapCode.Value = 0;
                                }
                                outputRaster.WriteBufferPixel();
                            }
                        }

                        if (!(mortMapNames == null))
                        {
                   
                            //----- Write Cohort Mortality Maps (number dead cohorts for selected species) --------
                            string path2 = MapNames.ReplaceTemplateVars(mortMapNames, activeAgent.AgentName, ModelCore.CurrentTime);
                            modelCore.UI.WriteLine("   Writing cohort mortality map to {0} ...", path2);
                            using (IOutputRaster<ShortPixel> outputRaster = modelCore.CreateRaster<ShortPixel>(path2, modelCore.Landscape.Dimensions))
                            {
                                ShortPixel pixel = outputRaster.BufferPixel;
                                foreach (Site site in ModelCore.Landscape.AllSites)
                                {
                                    if (site.IsActive)
                                    {
                                        pixel.MapCode.Value = (short)(SiteVars.NumberMortSppKilled[site][agentIndex]); 
                                    }
                                    else
                                    {
                                        //Inactive site
                                        pixel.MapCode.Value = -999; //should work with "short" type
                                    }
                                    outputRaster.WriteBufferPixel();
                                }
                            }
                        }

                        eventCount++;
                    }
                }                    
                else if (activeAgent.DispersalType == DispersalType.DYNAMIC)
                {
                    /*****************TODO*******************/
                    Console.WriteLine("Dynamic dispersal type has not been implemented yet!!");
                }

                agentIndex++;
            }
        }

        private void LogEvent(int currentTime,
                             Epidemic CurrentEvent,
                             IAgent agent)
        {
            EventLog.Clear();
            EventsLog el = new EventsLog();

            el.Time = currentTime;
            el.AgentName = agent.AgentName;
            el.InfectedSites = CurrentEvent.TotalSitesInfected;  //total number of infected sites
            el.DiseasedSites = CurrentEvent.TotalSitesDiseased;  //total number of diseased sites
            el.DamagedSites = CurrentEvent.TotalSitesDamaged;    //total number of damaged (i.e. with mortality) sites
            el.TotalCohortsKilled = CurrentEvent.TotalCohortsKilled; //total number of cohorts killed (all species)
            el.CohortsMortSppKilled = CurrentEvent.MortSppCohortsKilled; //total number of cohorts killed (species of interest)

            EventLog.AddObject(el);
            EventLog.WriteToFile();
        }

        public static void SerializeAsBincode(string outputPath, int timestep, double[] data) {
            (int x, int y) landscapeDimensions = (PlugIn.ModelCore.Landscape.Dimensions.Columns, PlugIn.ModelCore.Landscape.Dimensions.Rows);
            int width = landscapeDimensions.x;
            int height = landscapeDimensions.y;
            ulong count = (ulong)((long)width * (long)height);
            if (data == null) throw new ArgumentNullException(nameof(data));
            if ((ulong)data.LongLength != count) throw new ArgumentException("Data length does not match width*height.");
            string dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using (FileStream fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (BinaryWriter writer = new BinaryWriter(fs)) {
                writer.Write((uint)timestep);
                writer.Write((uint)width);
                writer.Write((uint)height);
                writer.Write(count);
                for (long i = 0; i < data.LongLength; i++) {
                    writer.Write(data[i]);
                }
            }
        }

        public static void SerializeAsBincode(string outputPath, int timestep, int[] data) {
            (int x, int y) landscapeDimensions = (PlugIn.ModelCore.Landscape.Dimensions.Columns, PlugIn.ModelCore.Landscape.Dimensions.Rows);
            int width = landscapeDimensions.x;
            int height = landscapeDimensions.y;
            ulong count = (ulong)((long)width * (long)height);
            if (data == null) throw new ArgumentNullException(nameof(data));
            if ((ulong)data.LongLength != count) throw new ArgumentException("Data length does not match width*height.");
            string dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using (FileStream fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (BinaryWriter writer = new BinaryWriter(fs)) {
                writer.Write((uint)timestep);
                writer.Write((uint)width);
                writer.Write((uint)height);
                writer.Write(count);
                for (long i = 0; i < data.LongLength; i++) {
                    writer.Write(data[i]);
                }
            }
        }

        public static void SerializeAsBincode(string outputPath, int timestep, List<(int x, int y)> healthySitesList, List<(int x, int y)> infectedSitesList, List<(int x, int y)> ignoredSitesList, ulong[] healthyBiomassTracker, ulong[] infectedBiomassTracker, ulong[] ignoredBiomassTracker) {
            (int x, int y) landscapeDimensions = (PlugIn.ModelCore.Landscape.Dimensions.Columns, PlugIn.ModelCore.Landscape.Dimensions.Rows);
            int width = landscapeDimensions.x;
            int height = landscapeDimensions.y;
            int expectedLength = width * height;
            if (healthySitesList == null) throw new ArgumentNullException(nameof(healthySitesList));
            if (infectedSitesList == null) throw new ArgumentNullException(nameof(infectedSitesList));
            if (ignoredSitesList == null) throw new ArgumentNullException(nameof(ignoredSitesList));
            if (healthyBiomassTracker == null) throw new ArgumentNullException(nameof(healthyBiomassTracker));
            if (infectedBiomassTracker == null) throw new ArgumentNullException(nameof(infectedBiomassTracker));
            if (ignoredBiomassTracker == null) throw new ArgumentNullException(nameof(ignoredBiomassTracker));
            if (healthyBiomassTracker.Length != expectedLength) throw new ArgumentException($"HealthyBiomassTracker length {healthyBiomassTracker.Length} does not match expected length {expectedLength} (width * height).");
            if (infectedBiomassTracker.Length != expectedLength) throw new ArgumentException($"InfectedBiomassTracker length {infectedBiomassTracker.Length} does not match expected length {expectedLength} (width * height).");
            if (ignoredBiomassTracker.Length != expectedLength) throw new ArgumentException($"IgnoredBiomassTracker length {ignoredBiomassTracker.Length} does not match expected length {expectedLength} (width * height).");
            string dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using (FileStream fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (BinaryWriter writer = new BinaryWriter(fs))
            {
                writer.Write((uint)timestep);
                writer.Write((uint)width);
                writer.Write((uint)height);
                writer.Write((ulong)healthySitesList.Count);
                for (int i = 0; i < healthySitesList.Count; i++) {
                    int x = healthySitesList[i].x;
                    int y = healthySitesList[i].y;
                    if (x < 0 || y < 0) throw new ArgumentOutOfRangeException("Coordinates must be non-negative.");
                    writer.Write((uint)x);
                    writer.Write((uint)y);
                }
                writer.Write((ulong)infectedSitesList.Count);
                for (int i = 0; i < infectedSitesList.Count; i++) {
                    int x = infectedSitesList[i].x;
                    int y = infectedSitesList[i].y;
                    if (x < 0 || y < 0) throw new ArgumentOutOfRangeException("Coordinates must be non-negative.");
                    writer.Write((uint)x);
                    writer.Write((uint)y);
                }
                writer.Write((ulong)ignoredSitesList.Count);
                for (int i = 0; i < ignoredSitesList.Count; i++) {
                    int x = ignoredSitesList[i].x;
                    int y = ignoredSitesList[i].y;
                    if (x < 0 || y < 0) throw new ArgumentOutOfRangeException("Coordinates must be non-negative.");
                    writer.Write((uint)x);
                    writer.Write((uint)y);
                }
                writer.Write((ulong)healthyBiomassTracker.Length);
                for (int i = 0; i < healthyBiomassTracker.Length; i++) {
                    writer.Write(healthyBiomassTracker[i]);
                }
                writer.Write((ulong)infectedBiomassTracker.Length);
                for (int i = 0; i < infectedBiomassTracker.Length; i++) {
                    writer.Write(infectedBiomassTracker[i]);
                }
                writer.Write((ulong)ignoredBiomassTracker.Length);
                for (int i = 0; i < ignoredBiomassTracker.Length; i++) {
                    writer.Write(ignoredBiomassTracker[i]);
                }
            }
        }

        private void InfectionStateDetection(IEnumerable<ActiveSite> sites, /* IInputParameters parameters, */ int landscapeX, int landscapeSize) {
            ulong[] healthyBiomassTracker = new ulong[landscapeSize];
            ulong[] infectedBiomassTracker = new ulong[landscapeSize];
            ulong[] ignoredBiomassTracker = new ulong[landscapeSize];
            List<(int x, int y)> healthySitesList = new List<(int x, int y)>();
            List<(int x, int y)> infectedSitesList = new List<(int x, int y)>();
            List<(int x, int y)> ignoredSitesList = new List<(int x, int y)>();
            foreach (ActiveSite site in sites) {
                //0 susceptible, 1 infected, 2 diseased
                byte status = SiteVars.InfStatus[site][0];

                int healthyBiomass = 0;
                int infectedBiomass = 0;
                int ignoredBiomass = 0;
                bool containsHealthySpecies = false;
                bool containsInfectedSpecies = false;
                foreach (ISpeciesCohorts speciesCohorts in SiteVars.Cohorts[site]) {
                    if (hostSpecies.Contains(speciesCohorts.Species)) {
                        containsHealthySpecies = true;
                    } else if (vulnerableSpecies.Contains(speciesCohorts.Species) && (status == 1 || status == 2)) {
                        containsInfectedSpecies = true;
                    }
                }
                int totalBiomass = healthyBiomass + infectedBiomass + ignoredBiomass;
                Location siteLocation = site.Location;
                int index = (siteLocation.Row - 1) * landscapeX + (siteLocation.Column - 1);
                if (containsHealthySpecies && !containsInfectedSpecies) {
                    healthySitesList.Add((siteLocation.Column, siteLocation.Row));
                } else if (containsInfectedSpecies) {
                    infectedSitesList.Add((siteLocation.Column, siteLocation.Row));
                } else {
                    ignoredSitesList.Add((siteLocation.Column, siteLocation.Row));
                }
                if (healthyBiomass < 0 || infectedBiomass < 0 || ignoredBiomass < 0) {
                    throw new ArgumentException($"Negative biomass detected: healthy={healthyBiomass}, infected={infectedBiomass}, ignored={ignoredBiomass}");
                }
                healthyBiomassTracker[index] = (ulong)healthyBiomass;
                infectedBiomassTracker[index] = (ulong)infectedBiomass;
                ignoredBiomassTracker[index] = (ulong)ignoredBiomass;
            }

            {
                Task.Run(() => {
                    Stopwatch outputStopwatch = new Stopwatch();
                    outputStopwatch.Start();
                    try {
                        string outputPath = $"./data/infection/{modelCore.CurrentTime}.bin";
                        SerializeAsBincode(outputPath, modelCore.CurrentTime, healthySitesList, infectedSitesList, ignoredSitesList, healthyBiomassTracker, infectedBiomassTracker, ignoredBiomassTracker);
                    }
                    catch (Exception ex) {
                        Log.Error(LogType.General, $"Debug bitmap generation failed: {ex.Message}");
                        throw;
                    }
                    outputStopwatch.Stop();
                    Log.Info(LogType.General, $"      Finished outputting infection state: {outputStopwatch.ElapsedMilliseconds} ms");
                });
            }
        }
    }
}
