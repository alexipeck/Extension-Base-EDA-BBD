//  Copyright 2016 North Carolina State University, Center for Geospatial Analytics & 
//  Forest Service Northern Research Station, Institute for Applied Ecosystem Studies
//  Authors:  Francesco Tonini, Brian R. Miranda, Chris Jones

using Landis.Core;
using Landis.Utilities;
using System.Collections.Generic;
using System.Text;

namespace Landis.Extension.EDA.BBD
{
    /// <summary>
    /// A parser that reads the extension parameters from text input.
    /// </summary>
    public class InputParameterParser
        : TextParser<IInputParameters>
    {
        public static IEcoregionDataset EcoregionsDataset = null;

        //---------------------------------------------------------------------
        public override string LandisDataValue
        {
            get
            {
                return PlugIn.ExtensionName;
            }

        }

        //---------------------------------------------------------------------
        public InputParameterParser()
        {
        }

        //---------------------------------------------------------------------

        protected override IInputParameters Parse()
        {

            InputVar<string> landisData = new InputVar<string>("LandisData");
            ReadVar(landisData);
            if (landisData.Value.Actual != PlugIn.ExtensionName)
                throw new InputValueException(landisData.Value.String, "The value is not \"{0}\"", PlugIn.ExtensionName);

            InputParameters parameters = new InputParameters();

            InputVar<int> timestep = new InputVar<int>("Timestep");
            ReadVar(timestep);
            parameters.Timestep = timestep.Value;

            //----------------------------------------------------------
            // Read in Maps and Log file names.

            // - infection status (0=Susceptible;1=Infected;2=Diseased). -
            InputVar<string> statusMapNames = new InputVar<string>("MapNames");
            ReadVar(statusMapNames);
            parameters.StatusMapNames = statusMapNames.Value; //check why this is different from others below

            // - mortality -
            InputVar<string> mortMapNames = new InputVar<string>("MORTMapNames");
            try
            {
                ReadVar(mortMapNames);
                parameters.MortMapNames = mortMapNames.Value;
            }
            catch (LineReaderException errString)
            {
                if (!(errString.MultiLineMessage[1].Contains("Found the name \"EPDMapNames\" but expected \"MORTMapNames\"")))
                {
                    throw errString;
                }

            }

            // - logfile -
            InputVar<string> logFile = new InputVar<string>("LogFile");
            ReadVar(logFile);
            parameters.LogFileName = logFile.Value;

            // - species order -
            InputVar<string> speciesOrderFile = new InputVar<string>("SpeciesOrder");
            ReadVar(speciesOrderFile);
            string speciesOrderPath = speciesOrderFile.Value;
            var speciesOrderList = new List<string>();
            var speciesDataset = PlugIn.ModelCore.Species;
            int lineNum = 0;
            foreach (var line in System.IO.File.ReadLines(speciesOrderPath))
            {
                lineNum++;
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                var found = false;
                foreach (var species in speciesDataset)
                {
                    if (species.Name == trimmed)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    throw new InputValueException(trimmed, $"Species '{trimmed}' on line {lineNum} of SpeciesOrder file does not exist in scenario species list.");
                }
                speciesOrderList.Add(trimmed);
            }
            if (speciesOrderList.Count == 0)
            {
                throw new InputValueException(speciesOrderPath, "SpeciesOrder file must contain at least one valid species name.");
            }
            parameters.SpeciesOrder = speciesOrderList;

            //----------------------------------------------------------
            // Last, read in Agent File names,
            // then parse the data in those files into agent parameters.

            InputVar<string> agentFileName = new InputVar<string>("EDAInputFiles");
            ReadVar(agentFileName);

            List<IAgent> agentParameterList = new List<IAgent>();
            AgentParameterParser agentParser = new AgentParameterParser();

            IAgent agentParameters = Landis.Data.Load<IAgent>(agentFileName.Value, agentParser);
            agentParameterList.Add(agentParameters);

            while (!AtEndOfInput) {
                StringReader currentLine = new StringReader(CurrentLine);

                ReadValue(agentFileName, currentLine);

                agentParameters = Landis.Data.Load<IAgent>(agentFileName.Value, agentParser);

                agentParameterList.Add(agentParameters);

                GetNextLine();

            }

            foreach(IAgent activeAgent in agentParameterList)
            {
                if(agentParameters == null)
                    PlugIn.ModelCore.UI.WriteLine("PARSE:  Agent Parameters NOT loading correctly.");
                else
                    PlugIn.ModelCore.UI.WriteLine("Name of Agent = {0}", agentParameters.AgentName);

            }
            parameters.ManyAgentParameters = agentParameterList;

            return parameters; //.GetComplete();

        }
    }
}
