using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace EdB.PrepareCarefully {
    public class InjuryOption {
        protected HediffDef hediffDef;
        protected HediffGiver hediffGiver;
        protected bool oldInjury = false;
        protected bool removesPart = false;
        protected bool wholeBody = false;
        protected string label = "?";

        public bool UsesSeverityPercentile {
            get {
                return hediffDef?.stages != null && hediffDef.stages.Count > 0;
            }
        }

        protected List<BodyPartDef> validParts = null;

        public InjuryOption() {
        }

        public HediffDef HediffDef {
            get { return hediffDef; }
            set { hediffDef = value; }
        }

        public HediffGiver Giver {
            get { return hediffGiver; }
            set { hediffGiver = value; }
        }

        public bool IsOldInjury {
            get { return oldInjury; }
            set { oldInjury = value; }
        }

        public bool IsAddiction {
            get {
                if (hediffDef.hediffClass != null && typeof(Hediff_Addiction).IsAssignableFrom(hediffDef.hediffClass)) {
                    return true;
                }
                return false;
            }
        }

        public bool RemovesPart {
            get { return removesPart; }
            set { removesPart = value; }
        }
        
        public string Label {
            get { return label; }
            set { label = value; }
        }

        public bool WholeBody {
            get { return wholeBody; }
            set { wholeBody = value; }
        }

        public List<BodyPartDef> ValidParts {
            get { return validParts; }
            set { validParts = value; }
        }

        public bool HasStageLabel {
            get {
                if (hediffDef.stages == null || hediffDef.stages.Count <= 1) {
                    return false;
                }
                if (IsAddiction) {
                    return false;
                }
                return true;
            }
        }
    }
}

