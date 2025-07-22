using Landis.Core;
using Landis.Library.UniversalCohorts;
using System.Collections.Generic;

namespace Landis.Extension.EDA.BBD
{
    public static class BBDProcessor
    {
        public static void ProcessSiteCohorts(Landis.SpatialModeling.ActiveSite site, ICore modelCore)
        {
            var siteCohorts = SiteVars.Cohorts[site];
            var biomassTransfer = new Dictionary<(ISpecies species, ushort age), int>();
            foreach (ISpeciesCohorts speciesCohorts in siteCohorts)
            {
                foreach (ICohort cohort in speciesCohorts)
                {
                    if (site.Location.Row == 52 && site.Location.Column == 9)
                    {
                        if (speciesCohorts.Species.Name == "Umbecali" || speciesCohorts.Species.Name == "Queragri" || speciesCohorts.Species.Name == "Acermacr" || speciesCohorts.Species.Name == "Aesccali")
                        {
                            modelCore.UI.WriteLine($"Site: ({site.Location.Row},{site.Location.Column}), Species: {speciesCohorts.Species.Name}, Age: {cohort.Data.Age}, Biomass: {cohort.Data.Biomass}");
                        }
                    }
                    if (speciesCohorts.Species.Name == "Umbecali")
                    {
                        int transfer = (int)(cohort.Data.Biomass * 0.3);
                        if (transfer > 0)
                        {
                            cohort.ChangeBiomass(-transfer);
                            biomassTransfer[(GetSpeciesByName("Queragri", modelCore), cohort.Data.Age)] = transfer;
                        }
                    }
                }
            }
            foreach (var entry in biomassTransfer)
            {
                var (targetSpecies, age) = entry.Key;
                int transfer = entry.Value;
                foreach (var speciesCohorts in siteCohorts)
                {
                    bool found = false;
                    if (speciesCohorts.Species == targetSpecies)
                    {
                        foreach (var cohort in speciesCohorts)
                        {
                            if (cohort.Data.Age == age)
                            {
                                cohort.ChangeBiomass(transfer);
                                found = true;
                                break;
                            }
                        }
                        if (found) break;
                        //NOTE: Will not be required after we switch from NECN-Succession to ForCS-Succession
                        //Continuing to test with NECN-Succession will pose a problem,
                        //but I can assume 20-80 leaf to wood biomass for the sake of it 
                        System.Dynamic.ExpandoObject woodLeafBiomasses = new System.Dynamic.ExpandoObject();
                        dynamic tempObject = woodLeafBiomasses;
                        tempObject.WoodBiomass = transfer * 0.8;
                        tempObject.LeafBiomass = transfer * 0.2;
                        siteCohorts.AddNewCohort(targetSpecies, age, transfer, woodLeafBiomasses);
                        break;
                    }
                }
            }
        }

        //TODO: Redo using a dictionary, had bad O(n) time complexity
        private static ISpecies GetSpeciesByName(string name, ICore modelCore) {
            foreach (var species in modelCore.Species) {
                if (species.Name == name) return species;
            }
            throw new KeyNotFoundException($"Species with name '{name}' not found.");
        }
    }
} 