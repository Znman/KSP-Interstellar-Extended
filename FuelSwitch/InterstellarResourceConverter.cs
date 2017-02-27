﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace InterstellarFuelSwitch
{
    class ResourceStats
    {
        public PartResourceDefinition definition;
        public double maxAmount = 0;
        public double currentAmount = 0;
        public double amountRatio = 0;
        public double retrieve = 0;
        public double transferRate = 1;
        public double normalizedDensity = 1;
        public double conversionRatio = 1;
    }

    class InterstellarEquilibrium : InterstellarResourceConverter  { }

    class InterstellarResourceConverter : PartModule
    {
        // persistant control
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Convert", guiUnits = "%"), UI_FloatRange()]
        public float convertPercentage = 0;

        // configs
        [KSPField]
        public bool showControlToggle = false;
        [KSPField]
        public string sliderText = string.Empty;
        [KSPField]
        public float percentageMaxValue = 100;
        [KSPField]
        public float percentageMinValue = -100;
        [KSPField]
        public float percentageStepIncrement = 5;
        [KSPField]
        public bool percentageSymetry = true;
        [KSPField]
        public string primaryResourceNames = string.Empty;
        [KSPField]
        public string secondaryResourceNames = string.Empty;
        [KSPField]
        public string primaryMolarMasses = string.Empty;
        //[KSPField]
        //public double molarHeatOfVaporization = 1;
        //[KSPField]
        //public double molarMass = 1;
        [KSPField]
        public double primaryConversionEnergyCost = 500;
        [KSPField]
        public double secondaryConversionEnergyCost = 1000;

        //[KSPField]
        //public double maxTransferAmountPrimary = 1;
        //[KSPField]
        //public double maxTransferAmountSecondary = 1;
        //[KSPField]
        public double maxPowerPrimary = 10;
        [KSPField]
        public double maxPowerSecondary = 10;
        [KSPField]
        public bool requiresPrimaryLocalInEditor = true;
        [KSPField]
        public bool requiresPrimaryLocalInFlight = true;

        PartResourceDefinition definitionElectricCharge;
        BaseField convertPercentageField;
        List<ResourceStats> primaryResources;
        List<ResourceStats> secondaryResources;
        UI_FloatRange convertPecentageEditorFloatRange;
        UI_FloatRange convertPecentageFlightFloatRange;

        bool hasNullDefinitions = false;
        bool retreivePrimary;
        bool retrieveSecondary;
        //bool maxTransferAmountPrimaryIsMissing = false;
        //bool maxTransferAmountSecondaryIsMissing = false;

        public override void OnStart(PartModule.StartState state)
        {
            definitionElectricCharge = PartResourceLibrary.Instance.GetDefinition("ElectricCharge");

            convertPercentageField = Fields["convertPercentage"];

            //maxTransferAmountPrimaryIsMissing = maxTransferAmountPrimary <= 0;
            //maxTransferAmountSecondaryIsMissing = maxTransferAmountSecondary <= 0;

            primaryResources = primaryResourceNames.Split(';').Select(m => new ResourceStats() { definition = PartResourceLibrary.Instance.GetDefinitionSafe(m.Trim()) } ).ToList();
            secondaryResources = secondaryResourceNames.Split(';').Select(m => new ResourceStats() { definition = PartResourceLibrary.Instance.GetDefinitionSafe(m.Trim()) }).ToList();

            hasNullDefinitions = primaryResources.Any(m => m.definition == null) || secondaryResources.Any(m => m.definition == null);
            if (hasNullDefinitions)
            {
                convertPercentageField.guiActiveEditor = false;
                convertPercentageField.guiActive = false;
                return;
            }

            foreach (var resource in primaryResources)
            {
                if (resource.definition.density > 0 && resource.definition.volume > 0)
                    resource.normalizedDensity = resource.definition.density / resource.definition.volume;
            }

            foreach (var resource in secondaryResources)
            {
                if (resource.definition.density > 0 && resource.definition.volume > 0)
                    resource.normalizedDensity = resource.definition.density / resource.definition.volume;
            }

            if (primaryResources.Count == 1 && secondaryResources.Count == 1)
            {
                var primary = primaryResources.First();
                var secondary = secondaryResources.First();

                if (primary.normalizedDensity > 0 && secondary.normalizedDensity > 0)
                {
                    secondary.conversionRatio = (primary.normalizedDensity * primary.definition.volume) / (secondary.normalizedDensity * secondary.definition.volume);
                    primary.conversionRatio = (secondary.normalizedDensity * secondary.definition.volume) / (primary.normalizedDensity * primary.definition.volume);
                }
                else if (primary.definition.unitCost > 0 && secondary.definition.unitCost > 0)
                {
                    secondary.conversionRatio = primary.definition.unitCost / secondary.definition.unitCost;
                    primary.conversionRatio = secondary.definition.unitCost / primary.definition.unitCost;
                }
            }
            
            // if slider text is missing, generate it
            if (string.IsNullOrEmpty(sliderText))
                convertPercentageField.guiName = String.Join("+", primaryResources.Select(m => m.definition.name).ToArray()) + "<->" + String.Join("+", secondaryResources.Select(m => m.definition.name).ToArray());
            else
                convertPercentageField.guiName = sliderText;

            convertPecentageFlightFloatRange = convertPercentageField.uiControlFlight as UI_FloatRange;
            convertPecentageFlightFloatRange.maxValue = percentageMaxValue;
            convertPecentageFlightFloatRange.minValue = percentageMinValue;
            convertPecentageFlightFloatRange.stepIncrement = percentageStepIncrement;
            convertPecentageFlightFloatRange.affectSymCounterparts = percentageSymetry ? UI_Scene.All : UI_Scene.None;

            convertPecentageEditorFloatRange = convertPercentageField.uiControlEditor as UI_FloatRange;
            convertPecentageEditorFloatRange.maxValue = percentageMaxValue;
            convertPecentageEditorFloatRange.minValue = percentageMinValue;
            convertPecentageEditorFloatRange.stepIncrement = percentageStepIncrement;
            convertPecentageEditorFloatRange.affectSymCounterparts = percentageSymetry ? UI_Scene.All : UI_Scene.None;
        }

        public void Update()
        {
            // exit if definition was not found
            if (hasNullDefinitions)
                return;

            // quit if any definition is missing
            if (!primaryResources.All(m => m.definition != null))
            {
                convertPercentageField.guiActive = false;
                return;
            }

            // in edit mode only show when primary resources are present
            if (requiresPrimaryLocalInEditor && HighLogic.LoadedSceneIsEditor)
            {
                convertPercentageField.guiActiveEditor = primaryResources.All(m => part.Resources.Contains( m.definition.id));
                return;
            }

            retreivePrimary = false;
            retrieveSecondary = false;

            // in flight mode, hide control if primary resources are not present 
            if (requiresPrimaryLocalInFlight && HighLogic.LoadedSceneIsFlight && !primaryResources.All(m => part.Resources.Contains(m.definition.id)))
            {
                 // hide interface and exit
                 convertPercentageField.guiActive = false;
                 return;
            }

            if (HighLogic.LoadedSceneIsEditor)
                return;

            foreach (var resource in primaryResources)
            {
                double currentAmount;
                double maxAmount;

                part.GetConnectedResourceTotals(resource.definition.id, out currentAmount, out maxAmount);

                if (maxAmount == 0)
                {
                    convertPercentageField.guiActive = false;
                    return;
                }

                resource.currentAmount = currentAmount;
                resource.maxAmount = maxAmount;
            }

            foreach (var resource in secondaryResources)
            {
                double currentAmount;
                double maxAmount;
                part.GetConnectedResourceTotals(resource.definition.id, out currentAmount, out maxAmount);

                if (maxAmount == 0)
                {
                    convertPercentageField.guiActive = false;
                    return;
                }

                resource.currentAmount = currentAmount;
                resource.maxAmount = maxAmount;
            }

            convertPercentageField.guiActive = true;

            if (convertPercentage == 0)
                return;

            primaryResources.ForEach(m => m.transferRate = maxPowerPrimary / primaryConversionEnergyCost / 1000 / m.definition.density);
            secondaryResources.ForEach(m => m.transferRate = maxPowerSecondary / secondaryConversionEnergyCost / 1000 / m.definition.density);

            primaryResources.ForEach(m => m.amountRatio = m.currentAmount / m.maxAmount);
            secondaryResources.ForEach(m => m.amountRatio = m.currentAmount / m.maxAmount);

            var percentageRatio = Math.Abs(convertPercentage) / 100d;
            var percentageRatioRemaining = 1 - percentageRatio;

            if (convertPercentage > 0 )
            {
                if (secondaryResources.Any(m => percentageRatio > m.amountRatio))
                {
                    retreivePrimary = true;
                    var neededAmount = secondaryResources.Min(m => Math.Max(percentageRatio - m.amountRatio, 0) * m.maxAmount / m.conversionRatio);
                    primaryResources.ForEach(m => m.retrieve = neededAmount);
                }
                else if (percentageMinValue < 0)
                {
                    retrieveSecondary = true;
                    var availableSpaceInTarget = primaryResources.Min(m => (m.maxAmount - m.currentAmount) / m.conversionRatio);
                    secondaryResources.ForEach(m => m.retrieve = Math.Min((Math.Max(m.amountRatio - percentageRatio, 0)) * m.maxAmount, availableSpaceInTarget));
                }
            }
            else if (convertPercentage < 0)
            {
                if (primaryResources.Any(m => percentageRatioRemaining < m.amountRatio))
                {
                    retrieveSecondary = true;

                    var availableSpaceInTarget = primaryResources.Min(m => (m.maxAmount - m.currentAmount) / m.conversionRatio);
                    secondaryResources.ForEach(m => m.retrieve = Math.Min((Math.Max(m.amountRatio - percentageRatioRemaining, 0)) * m.maxAmount, availableSpaceInTarget));
                }
                else if (percentageMaxValue > 0)
                {
                    retreivePrimary = true;
                    var availableSpaceInTarget = secondaryResources.Min(m => (m.maxAmount - m.currentAmount) / m.conversionRatio);
                    primaryResources.ForEach(m => m.retrieve = Math.Min((Math.Max(m.amountRatio - percentageRatioRemaining, 0)) * m.maxAmount, availableSpaceInTarget));
                }
            }
        }

        public void FixedUpdate()
        {
            if (HighLogic.LoadedSceneIsEditor)
                return;

            if (retreivePrimary && primaryResources.Any(r => r.retrieve > 0))
            {
                foreach (var resource in primaryResources)
                {
                    var fixedTransferRate = resource.transferRate * TimeWarp.fixedDeltaTime;
                    var transferRatio = resource.retrieve >= fixedTransferRate ? 1 : resource.retrieve / fixedTransferRate;

                    double powerRatio = 1;
                    if (maxPowerPrimary != 0)
                    {
                        var requestedPower = transferRatio * maxPowerPrimary * TimeWarp.fixedDeltaTime;
                        var receivedPower = part.RequestResource(definitionElectricCharge.id, requestedPower);
                        powerRatio = receivedPower / requestedPower;
                    }

                    var fixedRequest = Math.Min(transferRatio, resource.retrieve);
                    var receivedAmount = part.RequestResource(resource.definition.id, fixedRequest * powerRatio);

                    double createdAmount = 0;
                    foreach(var secondary in secondaryResources)
                    {
                        createdAmount += part.RequestResource(secondary.definition.id, -receivedAmount * secondary.conversionRatio) / secondary.conversionRatio;
                    }

                    var resturned = part.RequestResource(resource.definition.id, createdAmount + receivedAmount);
                    resource.retrieve = resource.retrieve - receivedAmount - resturned;
                }
            }
            else if (retrieveSecondary && secondaryResources.Any(r => r.retrieve > 0))
            {
                foreach (var resource in secondaryResources)
                {
                    var fixedTransferRate = resource.transferRate * TimeWarp.fixedDeltaTime;
                    var transferRatio = resource.retrieve >= fixedTransferRate ? 1 : resource.retrieve / fixedTransferRate;

                    double powerRatio = 1;
                    if (maxPowerPrimary != 0)
                    {
                        var requestedPower = transferRatio * maxPowerSecondary * TimeWarp.fixedDeltaTime;
                        var receivedPower = part.RequestResource(definitionElectricCharge.id, requestedPower);
                        powerRatio = receivedPower / requestedPower;
                    }

                    var fixedRequest = Math.Min(fixedTransferRate, resource.retrieve);
                    var receivedAmount = part.RequestResource(resource.definition.id, fixedRequest * powerRatio);

                    double createdAmount = 0;
                    foreach (var primary in primaryResources)
                    {
                        createdAmount += part.RequestResource(primary.definition.id, -receivedAmount * primary.conversionRatio) / primary.conversionRatio;
                    }

                    var resturned = part.RequestResource(resource.definition.id, createdAmount + receivedAmount);
                    resource.retrieve = resource.retrieve - receivedAmount - resturned;
                }
            }
        }

        public override string GetInfo()
        {
            return "Primary: " + primaryResourceNames + "\n"
                 + "Secondary: " + secondaryResourceNames ;
        }
    }
}