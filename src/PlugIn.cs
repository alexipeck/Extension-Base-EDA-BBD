//  Copyright 2016 North Carolina State University, Center for Geospatial Analytics & 
//  Forest Service Northern Research Station, Institute for Applied Ecosystem Studies
//  Authors:  Francesco Tonini, Brian R. Miranda, Chris Jones

using Landis.Core;
using Landis.Library.Metadata;
using Landis.SpatialModeling;
using System.Collections.Generic;
using System.Linq;
using System;
using Microsoft.CSharp;
using Landis.Extension.EDA.BBD;

namespace Landis.Extension.EDA.BBD
{
    ///<summary>
    /// A disturbance plug-in that simulates Pathogen Dispersal and Disease.
    /// </summary>

    public class PlugIn
        : ExtensionMain
    {
        public static readonly ExtensionType type = new ExtensionType("disturbance:eda.bbd");
        public static readonly string ExtensionName = "EDA.BBD";
        public static MetadataTable<EventsLog> EventLog;
        public static ExternalClimateData loadedClimateData;

        private string statusMapName; 
        private string mortMapNames;

        private IEnumerable<IAgent> manyAgentParameters;
        private static IInputParameters parameters;
        private bool reinitialized;
        

        //---------------------------------------------------------------------

        public PlugIn()
            : base(ExtensionName, type)
        {
        }

        //---------------------------------------------------------------------

        public override void LoadParameters(string dataFile,
                                            ICore mCore)
        {
            ModelCore = mCore;
            InputParameterParser.EcoregionsDataset = ModelCore.Ecoregions;
            InputParameterParser parser = new InputParameterParser();
            parameters = Landis.Data.Load<IInputParameters>(dataFile, parser);
        }

        //---------------------------------------------------------------------

        public static ICore ModelCore { get; private set; }
        public override void AddCohortData(){return;}


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

            //initialize site variables:
            int numAgents = parameters.ManyAgentParameters.Count();
            SiteVars.Initialize(ModelCore, numAgents);

            //Dispersal probdisp = new Dispersal();
            manyAgentParameters = parameters.ManyAgentParameters;
            int agentIndex = 0;

            //Initialize non-library climate data
            //loadedClimateData = ClimateData.ReadClimateData(manyAgentParameters);
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
            ModelCore.UI.WriteLine("   Processing landscape for EDA events ...");
            if(!reinitialized)
                InitializePhase2();

            //int eventCount = 0;

            //asdf;
            foreach (ActiveSite site in ModelCore.Landscape) {
                BBDProcessor.ProcessSiteCohorts(site, ModelCore);
            }

            // Directly index Queragri cohorts by age for transfer
            

            /* int agentIndex = 0;
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
                        ModelCore.UI.WriteLine("   Writing infection status map to {0} ...", path);
                        using (IOutputRaster<BytePixel> outputRaster = ModelCore.CreateRaster<BytePixel>(path, ModelCore.Landscape.Dimensions))
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
                            ModelCore.UI.WriteLine("   Writing cohort mortality map to {0} ...", path2);
                            using (IOutputRaster<ShortPixel> outputRaster = ModelCore.CreateRaster<ShortPixel>(path2, ModelCore.Landscape.Dimensions))
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
                    //TODO
                    Console.WriteLine("Dynamic dispersal type has not been implemented yet!!");
                }

                agentIndex++;
            } */
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

        // Helper method to get ISpecies by name
        private ISpecies GetSpeciesByName(string name) {
            foreach (var species in ModelCore.Species) {
                if (species.Name == name) return species;
            }
            return null;
        }
    }
}
