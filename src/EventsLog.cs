﻿//  Copyright 2016 North Carolina State University, Center for Geospatial Analytics & 
//  Forest Service Northern Research Station, Institute for Applied Ecosystem Studies
//  Authors:  Francesco Tonini, Brian R. Miranda, Chris Jones

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Landis.Library.Metadata;


namespace Landis.Extension.EDA.BBD
{
    public class EventsLog
    {
      
        //Specify attributes to be written in the log file?
        [DataFieldAttribute(Unit = FieldUnits.Year, Desc = "Simulation Year")]
        public int Time {set; get;}

        [DataFieldAttribute(Desc = "Agent Name")]
        public string AgentName { set; get; }

        [DataFieldAttribute(Unit = FieldUnits.Count, Desc = "Number of Infected Sites in Event")]
        public int InfectedSites { set; get; }

        [DataFieldAttribute(Unit = FieldUnits.Count, Desc = "Number of Diseased Sites in Event")]
        public int DiseasedSites { set; get; }

        [DataFieldAttribute(Unit = FieldUnits.Count, Desc = "Number of Damaged Sites (Mortality) in Event")]
        public int DamagedSites { set; get; }

        [DataFieldAttribute(Unit = FieldUnits.Count, Desc = "Number of Cohorts Killed (All Species) in Event")]
        public int TotalCohortsKilled { set; get; }

        [DataFieldAttribute(Unit = FieldUnits.Count, Desc = "Number of Cohorts Killed (Selected Species) in Event")]
        public int CohortsMortSppKilled { set; get; }

    }
}
