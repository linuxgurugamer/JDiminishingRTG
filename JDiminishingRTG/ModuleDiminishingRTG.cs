/*
 * Copyright Jake "KwirkyJ" Smith) <kwirkyj.smith0@gmail.com>
 * 
 * Available for use under the LGPL v3 license.
 */
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
//using UnityEngine.UI;

namespace JDiminishingRTG
{
    [KSPModule("Radioisotope Generator")]
    public class ModuleDiminishingRTG : PartModule
    {
        [KSPField(isPersistant = true)]
        public float volume = 5F;

        // TODO: efficiency curve (inverse logistic?)
        [KSPField(isPersistant = true)]
        public float efficiency = 0.5F;

        #region privateFields
        [KSPField(guiName = "Fuel type", isPersistant = true, guiActiveEditor = true, guiActive = true)]
        private string fuelName;

        [KSPField(guiName = "Half-life", isPersistant = true, guiActive = true, guiActiveEditor = true, guiUnits = " years")]
        private float fuelHalflife = -1F; // time in Kerbin-years

        [KSPField(isPersistant = true)]
        private float fuelPep = -1F;

        [KSPField(isPersistant = true)]
        private float fuelDensity = -1;

        [KSPField(isPersistant = true)]
        private float timeOfStart = -1F;

        [KSPField(guiName = "Output", guiActive = true, guiActiveEditor = true, guiUnits = " Ec/s")]
        private string guiOutput;

        private float output;

        [KSPField(guiName = "RTG mass", isPersistant = true, guiActiveEditor = true, guiUnits = " tonnes")]
        private float mass = -1F;

        //[SerializeField] // changes a bug
        [KSPField(guiName = "Fuel type", isPersistant = true, guiActiveEditor = true)]
        [UI_ChooseOption(options = new[] { "none" }, affectSymCounterparts = UI_Scene.Editor, scene = UI_Scene.Editor)]//, suppressEditorShipModified = true)]
        private int fuelSelectorIndex = 0;
        #endregion

        #region staticFields
        private static bool AreConfigsRead = false;

        private static List<RTGFuelConfig> RTGFuelConfigList;

        private static bool GenerateElectricity = false;
        private static bool GenerateHeat = true;

        private static string PowerDensityUnits = "W/kg";
        private static string PowerDensityLabel = "pep";
        private static float PowerDensityFactor = 1e-3F;

        private static string HeatUnits = "W";
        private static string ElectricityUnits = "Ec";

        private static float HeatScale = 1F;
        private static float ElectricityScale = 1F;

        private static string[] FuelNames = new string[] { "None" };
        #endregion

        public override string GetInfo()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Output decays over time.\n\n");
            sb.Append(String.Format("Efficiency: {0:##.##}%\n", this.efficiency * 100));
            sb.Append(String.Format("Volume: {0:####.##} dL\n\n", this.volume));
            if (RTGFuelConfigList == null)
                ReadCustomConfigs();
            sb.Append("<color=#99FF00>Available fuels:</color>");
            for (int i = RTGFuelConfigList.Count - 1; i >= 0; i--)
            {
                RTGFuelConfig c = RTGFuelConfigList[i];
                sb.Append("\n    <color=#FF6600>" + c.resourceName + "</color>\n");
                sb.Append("        half-life: " + c.halflife + " years\n");
                sb.Append("        " + PowerDensityLabel + ": " + (c.pep * PowerDensityFactor)
                           + " " + PowerDensityUnits);
            }
            return sb.ToString();
        }

        public override void OnStart(StartState state)
        {
            if (!AreConfigsRead)
            {
                ReadCustomConfigs();
                PopulateFuelNames();
                AreConfigsRead = true;
            }

            if (HighLogic.LoadedScene == GameScenes.EDITOR)
            {
                UI_Control c = this.Fields["fuelSelectorIndex"].uiControlEditor;

                c.onFieldChanged = updateFuelSetup;
                UI_ChooseOption o = (UI_ChooseOption)c;
                o.options = FuelNames;
                this.updateFuelSetup(this.fuelSelectorIndex);
            }
            this.updateUIOutput(this.fuelPep, this.efficiency);
            if (HighLogic.LoadedScene == GameScenes.FLIGHT)
            {
                this.updateFuelSetup(this.fuelSelectorIndex);
                this.part.force_activate();
            }
        }

        private static void PopulateFuelNames()
        {

            string[] names = new string[RTGFuelConfigList.Count];
            for (int i = RTGFuelConfigList.Count - 1; i >= 0; i--)
            {
                RTGFuelConfig r = RTGFuelConfigList[i];
                //names[i] = r.resourceName;
                names[i] = r.resourceAbbr;

            }
            FuelNames = names;
        }

        #region configLoading
        //SEE http://docuwiki-kspapi.rhcloud.com/#/classes/UI_ChooseOption
        //    http://forum.kerbalspaceprogram.com/index.php?/topic/135891-ui_chooseoption-oddities-when-displaying-long-names/
        private static void ReadCustomConfigs()
        {
            Log.Info("Reading configs...");
            RTGFuelConfigList = GetRTGFuelConfigs(GameDatabase.Instance.GetConfigNodes("RTGFUELCONFIG"));
            try
            {
                foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("JDIMINISHINGRTGGLOBALCONFIG"))
                {
                    ReadJDiminishingRTGGlobalConfig(n);
                }
                Log.Info("...reading configs done.");
            }
            catch (Exception e)
            {
                Debug.LogError("[JDimRTG] Problem in reading global config!\n" + e.ToString());
            }
        }

        private static List<RTGFuelConfig> GetRTGFuelConfigs(ConfigNode[] database_rtgfuelnodes)
        {
            Log.Info("Reading RTG Fuel configs...");
            List<RTGFuelConfig> config_list = new List<RTGFuelConfig>();
            List<string> seen_resources = new List<string>();
            foreach (ConfigNode node in database_rtgfuelnodes)
            {
                try
                {
                    RTGFuelConfig c = new RTGFuelConfig(node);
                    if (!seen_resources.Contains(c.resourceName))
                    {
                        config_list.Add(c);
                        seen_resources.Add(c.resourceName);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError("[JDimRTG] Could not load RTGFUELCONFIG:\n" + e.ToString());
                }
            }
            return config_list;
        }

        private static void ReadJDiminishingRTGGlobalConfig(ConfigNode n)
        {
            Log.Info("Reading RTG global config...");
            if (n.HasValue("GenerateHeat"))
            {
                GenerateHeat = bool.Parse(n.GetValue("GenerateHeat"));
                Log.Info("GenerateHeat = " + GenerateHeat);
            }
            if (!GenerateHeat)
            {
                GenerateElectricity = true;
            }
            else if (n.HasValue("GenerateElectricity"))
            {
                GenerateElectricity = bool.Parse(n.GetValue("GenerateElectricity"));
                Log.Info("GenerateElectricity = " + GenerateElectricity);
            }

            if (n.HasValue("PowerDensityFactor"))
            {
                PowerDensityFactor = float.Parse(n.GetValue("PowerDensityFactor"));
                Log.Info("PowerDensityFactor = " + PowerDensityFactor);
            }
            if (n.HasValue("PowerDensityLabel"))
            {
                PowerDensityLabel = n.GetValue("PowerDensityLabel");
                Log.Info("PowerDensityLabel = " + PowerDensityLabel);
            }
            if (n.HasValue("PowerDensityUnits"))
            {
                PowerDensityUnits = n.GetValue("PowerDensityUnits");
                Log.Info("PowerDensityUnits = " + PowerDensityUnits);
            }

            if (n.HasValue("HeatUnits"))
            {
                HeatUnits = n.GetValue("HeatUnits");
                Log.Info("HeatUnits = " + HeatUnits);
            }
            if (n.HasValue("ElectricityUnits"))
            {
                ElectricityUnits = n.GetValue("ElectricityUnits");
                Log.Info("ElectricityUnits = " + ElectricityUnits);
            }

            if (n.HasValue("HeatScale"))
            {
                HeatScale = float.Parse(n.GetValue("HeatScale"));
                Log.Info("HeatScale = " + HeatScale);
            }
            if (n.HasValue("ElectricityScale"))
            {
                ElectricityScale = float.Parse(n.GetValue("ElectricityScale"));
                Log.Info("ElectricityScale = " + ElectricityScale);
            }
        }
        #endregion

        #region configurationLogic
        public void updateFuelSetup(BaseField field, object oldValue)
        {
            this.updateFuelSetup(this.fuelSelectorIndex);
        }
 
        private void updateFuelSetup(int index)
        {
            for (int i1 = RTGFuelConfigList.Count - 1; i1 >= 0; i1--)
            {
                RTGFuelConfig possibleFuel = RTGFuelConfigList[i1];
                var resources = this.part.Resources;
                for (int i = this.part.Resources.Count - 1; i >= 0; i--)
                {
                    PartResource r = this.part.Resources[i];
                    if (r.resourceName == possibleFuel.resourceName)
                    {
                        resources.Remove(r);
                    }
                }
            }

            RTGFuelConfig config = RTGFuelConfigList[index];
            this.fuelName = config.resourceName;
            this.fuelHalflife = config.halflife;
            this.fuelPep = config.pep;
            this.fuelDensity = config.density;

            ConfigNode resourceNode = new ConfigNode("RESOURCE");
            resourceNode.AddValue("name", this.fuelName);
            resourceNode.AddValue("maxAmount", this.volume);
            resourceNode.AddValue("amount", this.volume);
            resourceNode.AddValue("isTweakable", false);
            this.part.AddResource(resourceNode);

            this.mass = this.part.mass + this.part.GetResourceMass();
            this.updateUIOutput(this.fuelPep, this.volume * this.fuelDensity);
        }
        #endregion

        #region operation
        public override void OnUpdate()
        {
            this.updateUIOutput(this.output, this.efficiency);
        }

        private void updateUIOutput(float output, float efficiency)
        {
            float tmpout = output;
            string units = HeatUnits;
            if (GenerateElectricity)
            {
                tmpout = output * efficiency * ElectricityScale;
                units = ElectricityUnits;
            }
            if (tmpout < 1)
            {
                units = units + "/min";
                tmpout = tmpout * 60F;
            }
            else
            {
                units = units + "/s";
            }
            Fields["guiOutput"].guiUnits = " " + units;
            this.guiOutput = String.Format("{0:##.##}", tmpout);
        }

        public override void OnFixedUpdate()
        {
            float now = (float)Planetarium.GetUniversalTime();
            this.timeOfStart = (this.timeOfStart < 0) ? now : this.timeOfStart;

            PartResource r = this.getRTGResource(this.fuelName);
            if (r == null)
            {
                Debug.LogError("[JDimRTG] Module resource '" + this.fuelName + "' has no matching PartResource");
                return;
            }

            double progress = Math.Pow(2, -((now - this.timeOfStart) / (this.fuelHalflife * 9203545)));
            r.amount = r.maxAmount * progress;
            this.output = getOutput(this.fuelPep, (float)r.amount * this.fuelDensity);
            this.part.mass = this.mass;
            if (GenerateElectricity)
            {
                this.part.RequestResource("ElectricCharge", -this.output * this.efficiency * TimeWarp.fixedDeltaTime, false);
            }
            if (GenerateHeat)
            {
                this.part.AddThermalFlux(this.output);
            }
        }

        private PartResource getRTGResource(string resname)
        {
            for (int i = this.part.Resources.Count - 1; i >= 0; i--)
            {
                PartResource r = this.part.Resources[i];
                if (r.resourceName == resname)
                {
                    return r;
                }
            }
            return null;
        }

        private float getOutput(float pep, float res_mass)
        {
            return pep * res_mass * HeatScale;
        }
        #endregion
    }
}

