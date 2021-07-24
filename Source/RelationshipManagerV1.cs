using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace EdB.PrepareCarefully {
    public class RelationshipManagerV1 {
        protected Randomizer randomizer = new Randomizer();
        protected List<PawnRelationDef> allowedRelationships = new List<PawnRelationDef>();
        protected Dictionary<PawnRelationDef, PawnRelationDef> inverseRelationships = new Dictionary<PawnRelationDef, PawnRelationDef>();
        protected List<CustomRelationship> deletionList = new List<CustomRelationship>();
        protected List<CustomPawn> parentChildPawns = new List<CustomPawn>();
        protected Dictionary<Pawn, CustomPawn> pawnCustomPawnLookup = new Dictionary<Pawn, CustomPawn>();
        protected List<ParentChildGroup> parentChildGroups = new List<ParentChildGroup>();
        protected bool dirty = true;
        protected int HiddenParentChildIndex = 1;
        protected int TemporaryParentChildIndex = 1;

        private List<CustomPawn> temporaryPawns = new List<CustomPawn>();

        protected RelationshipList relationships = new RelationshipList();
        
        public RelationshipManagerV1(List<CustomPawn> customPawns) {
            PopulateAllowedRelationships();
            PopulateInverseRelationships();
            InitializeRelationshipsForStartingPawns(customPawns);
            // Add a male and a female pawn to the new hidden pawn list.
            temporaryPawns.Add(CreateNewTemporaryPawn(Gender.Female));
            temporaryPawns.Add(CreateNewTemporaryPawn(Gender.Male));
            // Assign indices to hidden pawns (indices are used to name pawns, i.e. "Unknown 1" and "Unknown 2").
            // We do this here (and not when we initially created the hidden pawns) so that the initial indices will
            // start at 1 and count up from there as they are displayed from left to right in the UI.
            ReassignHiddenPawnIndices();
        }

        public List<CustomPawn> TemporaryPawns {
            get {
                return temporaryPawns;
            }
        }

        public int NextHiddenParentChildIndex {
            get {
                return HiddenParentChildIndex++;
            }
        }

        public int NextTemporaryParentChildIndex {
            get {
                return TemporaryParentChildIndex++;
            }
        }

        protected void PopulateAllowedRelationships() {
            allowedRelationships.AddRange(DefDatabase<PawnRelationDef>.AllDefs.ToList().FindAll((PawnRelationDef def) => {
                if (def.familyByBloodRelation) {
                    return false;
                }
                CarefullyPawnRelationDef extended = DefDatabase<CarefullyPawnRelationDef>.GetNamedSilentFail(def.defName);
                if (extended != null && extended.animal) {
                    return false;
                }
                MethodInfo info = def.workerClass.GetMethod("CreateRelation", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (info == null) {
                    return false;
                }
                else {
                    return true;
                }
            }));
        }

        protected void PopulateInverseRelationships() {
            foreach (var def in DefDatabase<PawnRelationDef>.AllDefs) {
                PawnRelationDef inverse = null;
                CarefullyPawnRelationDef extended = DefDatabase<CarefullyPawnRelationDef>.GetNamedSilentFail(def.defName);
                if (extended != null && extended.inverse != null) {
                    inverse = DefDatabase<PawnRelationDef>.GetNamedSilentFail(extended.inverse);
                }
                else {
                    inverse = ComputeInverseRelationship(def);
                }
                if (inverse != null) {
                    inverseRelationships[def] = inverse;
                }
            }
        }

        public PawnRelationDef FindInverseRelationship(PawnRelationDef def) {
            PawnRelationDef inverse;
            if (inverseRelationships.TryGetValue(def, out inverse)) {
                return inverse;
            }
            else {
                return null;
            }
        }

        protected PawnRelationDef ComputeInverseRelationship(PawnRelationDef def) {
            Pawn source = randomizer.GenerateColonist();
            Pawn target = randomizer.GenerateColonist();
            MethodInfo info = def.workerClass.GetMethod("CreateRelation", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (info == null) {
                return null;
            }
            var worker = FindPawnRelationWorker(def);
            PawnGenerationRequest req = new PawnGenerationRequest();
            worker.CreateRelation(source, target, ref req);
            foreach (PawnRelationDef d in PawnRelationUtility.GetRelations(target, source)) {
                return d;
            }
            return null;
        }

        public PawnRelationWorker FindPawnRelationWorker(PawnRelationDef def) {
            CarefullyPawnRelationDef carefullyDef = DefDatabase<CarefullyPawnRelationDef>.GetNamedSilentFail(def.defName);
            if (carefullyDef == null || carefullyDef.workerClass == null) {
                return def.Worker;
            }
            else {
                PawnRelationWorker worker = carefullyDef.Worker;
                if (worker != null) {
                    //Logger.Debug("Returned carefully worker for " + def.defName + ", " + worker.GetType().FullName);
                    return carefullyDef.Worker;
                }
                else {
                    return def.Worker;
                }
            }
        }

        public List<ParentChildGroup> ParentChildGroups {
            get {
                return parentChildGroups;
            }
        }

        private void InitializeParentChildGroupsForStartingPawns(List<CustomPawn> customPawns) {
            // Create a map so that we can look up custom pawns based on their matching pawn.
            Dictionary<Pawn, CustomPawn> pawnToCustomPawnMap = new Dictionary<Pawn, CustomPawn>();
            int pawnCount = customPawns.Count;
            for (int i = 0; i < customPawns.Count; i++) {
                pawnToCustomPawnMap.Add(customPawns[i].Pawn, customPawns[i]);
            }
            
            // Go through each pawn and look for a child/parent relationship between it and all other pawns.
            Dictionary<Pawn, ParentChildGroup> groupLookup = new Dictionary<Pawn, ParentChildGroup>();
            foreach (CustomPawn child in customPawns) {
                foreach (var r in child.Pawn.relations.DirectRelations) {
                    if (r.def == PawnRelationDefOf.Parent) {
                        Pawn parent = r.otherPawn;
                        if (pawnToCustomPawnMap.ContainsKey(parent)) {
                            CustomPawn parentCustomPawn = pawnToCustomPawnMap[parent];
                            CustomPawn childCustomPawn = child;

                            // See if the child has an existing parent/child group.  If not, create the group.
                            // If so, just add the parent.
                            ParentChildGroup group;
                            if (!groupLookup.TryGetValue(child.Pawn, out group)) {
                                group = new ParentChildGroup();
                                group.Children.Add(childCustomPawn);
                                groupLookup.Add(child.Pawn, group);
                            }
                            group.Parents.Add(parentCustomPawn);
                        }
                        else {
                            Logger.Warning("pawnToCustomPawnMap did not contain key for " + parent.LabelShort);
                        }
                    }
                }
            }

            SortAndDedupeParentChildGroups(groupLookup.Values);
        }

        private void InitializeParentChildGroupRelationships(List<CustomRelationship> relationships) {
            Dictionary<CustomPawn, ParentChildGroup> groupLookup = new Dictionary<CustomPawn, ParentChildGroup>();
            foreach (var relationship in relationships) {
                CustomPawn parent = null;
                CustomPawn child = null;
                if (relationship.def == PawnRelationDefOf.Parent) {
                    child = relationship.source;
                    parent = relationship.target;
                }
                else if (relationship.def == PawnRelationDefOf.Child) {
                    child = relationship.target;
                    parent = relationship.source;
                }
                if (parent == null) {
                    Logger.Warning("Could not add relationship because of missing parent");
                    continue;
                }
                if (child == null) {
                    Logger.Warning("Could not add relationship because of missing child");
                    continue;
                }

                // See if the child has an existing parent/child group.  If not, create the group.
                // If so, just add the parent.
                ParentChildGroup group;
                if (!groupLookup.TryGetValue(child, out group)) {
                    group = new ParentChildGroup();
                    group.Children.Add(child);
                    groupLookup.Add(child, group);
                }
                group.Parents.Add(parent);
            }

            SortAndDedupeParentChildGroups(groupLookup.Values);
        }

        private void SortAndDedupeParentChildGroups(IEnumerable<ParentChildGroup> groups) {
            
            // Sort the parents.
            Dictionary<int, ParentChildGroup> parentLookup = new Dictionary<int, ParentChildGroup>();
            HashSet<ParentChildGroup> groupsToRemove = new HashSet<ParentChildGroup>();
            foreach (var group in groups) {
                group.Parents.Sort((CustomPawn a, CustomPawn b) => {
                    if (a == null || b == null) {
                        if (a == b) {
                            return 0;
                        }
                        else {
                            return (a == null) ? -1 : 1;
                        }
                    }
                    return a.Id.CompareTo(b.Id);
                });
            }

            // Generate a hash for the sorted list of parents, using a lookup to find groups
            // that have the same parents in them.  For any group with duplicate parents, copy
            // the children from that group into the existing group, and mark the duplicate
            // group for removal.
            foreach (var group in groups) {
                int hash = 0;
                foreach (var parent in group.Parents) {
                    hash = hash ^ EqualityComparer<string>.Default.GetHashCode(parent.Id);
                }
                ParentChildGroup existing;
                if (parentLookup.TryGetValue(hash, out existing)) {
                    //Logger.Debug("Removing duplicate group: " + group);
                    //Logger.Debug("  Duplicate of group: " + existing);
                    foreach (var child in group.Children) {
                        existing.Children.Add(child);
                    }
                    //Logger.Debug("  Added children from dupe: " + existing);
                    groupsToRemove.Add(group);
                }
                else {
                    parentLookup.Add(hash, group);
                }
            }

            // Create the final list, discarding the groups that were merged.
            List<ParentChildGroup> result = new List<ParentChildGroup>();
            foreach (var group in groups) {
                if (!groupsToRemove.Contains(group)) {
                    result.Add(group);
                    //Logger.Debug(group.ToString());
                }
            }
            
            parentChildGroups = result;
        }

        public void ReassignHiddenPawnIndices() {
            HiddenParentChildIndex = 1;
            TemporaryParentChildIndex = 1;
            foreach (var group in parentChildGroups) {
                foreach (var parent in group.Parents) {
                    if (parent.Type == CustomPawnType.Hidden && parent.Index == null) {
                        parent.Index = HiddenParentChildIndex++;
                    }
                    else if (parent.Type == CustomPawnType.Temporary && parent.Index == null) {
                        parent.Index = TemporaryParentChildIndex++;
                    }
                }
                foreach (var child in group.Children) {
                    if (child.Type == CustomPawnType.Hidden && child.Index == null) {
                        child.Index = HiddenParentChildIndex++;
                    }
                    else if (child.Type == CustomPawnType.Temporary && child.Index == null) {
                        child.Index = TemporaryParentChildIndex++;
                    }
                }
            }
            foreach (var r in relationships) {
                if (r.source.Type == CustomPawnType.Hidden && r.source.Index == null) {
                    r.source.Index = HiddenParentChildIndex++;
                }
                if (r.target.Type == CustomPawnType.Hidden && r.target.Index == null) {
                    r.target.Index = HiddenParentChildIndex++;
                }
            }
        }

        public IEnumerable<CustomPawn> ParentChildPawns {
            get {
                return parentChildPawns;
            }
        }

        public IEnumerable<CustomPawn> ColonyAndWorldPawns {
            get {
                return ParentChildPawns.Where((CustomPawn p) => {
                    return !p.Hidden;
                });
            }
        }

        public IEnumerable<CustomPawn> HiddenPawns {
            get {
                return ParentChildPawns.Where((CustomPawn p) => {
                    return p.Type == CustomPawnType.Hidden || p.Type == CustomPawnType.Temporary;
                });
            }
        }

        public CustomPawn AddHiddenParentChildPawn(CustomPawn customPawn) {
            parentChildPawns.Add(customPawn);
            pawnCustomPawnLookup.Add(customPawn.Pawn, customPawn);
            return customPawn;
        }

        public CustomPawn AddTemporaryParentChildPawn(CustomPawn customPawn) {
            parentChildPawns.Add(customPawn);
            pawnCustomPawnLookup.Add(customPawn.Pawn, customPawn);
            return customPawn;
        }

        public CustomPawn AddVisibleParentChildPawn(CustomPawn customPawn) {
            return AddVisibleParentChildPawn(customPawn.Pawn, customPawn);
        }

        public CustomPawn AddVisibleParentChildPawn(Pawn pawn, CustomPawn customPawn) {
            parentChildPawns.Add(customPawn);
            pawnCustomPawnLookup.Add(pawn, customPawn);
            return customPawn;
        }

        public void InitializeWithCustomPawns(IEnumerable<CustomPawn> pawns) {
            parentChildPawns.Clear();
            pawnCustomPawnLookup.Clear();

            // Create parent/child pawn records for each colonist.
            foreach (var pawn in pawns) {
                if (pawn.Type == CustomPawnType.Temporary) {
                    AddTemporaryParentChildPawn(pawn);
                }
                else if (pawn.Type == CustomPawnType.Hidden) {
                    AddHiddenParentChildPawn(pawn);
                }
                else {
                    AddVisibleParentChildPawn(pawn);
                }
            }
        }

        public void InitializeRelationshipsForStartingPawns(List<CustomPawn> customPawns) {
            InitializeParentChildGroupsForStartingPawns(customPawns);

            // Go through each pawn and check for relationships between it and all other pawns.
            foreach (CustomPawn pawn in customPawns) {
                foreach (CustomPawn other in customPawns) {
                    if (pawn == other) {
                        continue;
                    }

                    // Find the corresponding pawn facades.
                    CustomPawn thisCustomPawn = pawn;
                    CustomPawn otherCustomPawn = other;

                    // Go through each relationship between the two pawns.
                    foreach (PawnRelationDef def in PawnRelationUtility.GetRelations(pawn.Pawn, other.Pawn)) {
                        // Don't add blood relations.
                        if (def.familyByBloodRelation) {
                            continue;
                        }
                        if (def.implied) {
                            continue;
                        }
                        // Otherwise, if no relationship records exists for this relationship, add it.
                        if (!relationships.Contains(def, thisCustomPawn, otherCustomPawn)) {
                            relationships.Add(new CustomRelationship(def, FindInverseRelationship(def), thisCustomPawn, otherCustomPawn));
                        }
                    }
                }
            }
        }

        public void Clear() {
            this.parentChildPawns.Clear();
            this.pawnCustomPawnLookup.Clear();
            this.relationships.Clear();
            this.parentChildGroups.Clear();
            Clean();
        }
        
        public IEnumerable<PawnRelationDef> AllowedRelationships {
            get {
                return allowedRelationships;
            }
        }
        
        public IEnumerable<CustomRelationship> Relationships {
            get {
                return relationships;
            }
        }

        protected void Clean() {
            dirty = false;
        }

        protected void DeleteRelationships() {
            foreach (var r in deletionList) {
                relationships.Remove(r);
            }
            deletionList.Clear();
        }

        public void AddRelationship(PawnRelationDef def, CustomPawn source, CustomPawn target) {
            if (def.workerClass.GetMethod("CreateRelation", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly) == null) {
                return;
            }
            this.relationships.Add(new CustomRelationship(def, FindInverseRelationship(def), source, target));
            dirty = true;
        }

        public void AddRelationships(List<CustomRelationship> relationships) {
            List<CustomRelationship> parentChildRelationships = new List<CustomRelationship>();
            List<CustomRelationship> otherRelationships = new List<CustomRelationship>();
            foreach (var r in relationships) {
                if (r.def.defName == "Parent" || r.def.defName == "Child") {
                    parentChildRelationships.Add(r);
                }
                else {
                    otherRelationships.Add(r);
                }
            }
            foreach (var r in otherRelationships) {
                AddRelationship(r.def, r.source, r.target);
            }
            InitializeParentChildGroupRelationships(parentChildRelationships);
        }

        public void DeleteRelationship(CustomRelationship relationship) {
            this.relationships.Remove(relationship);
        }
        public void DeleteRelationship(PawnRelationDef def, CustomPawn source, CustomPawn target) {
            CustomRelationship toRemove = relationships.Find(def, source, target);
            if (toRemove != null) {
                deletionList.Add(toRemove);
            }
            dirty = true;
        }
        
        public void RemoveParentChildGroup(ParentChildGroup group) {
            parentChildGroups.Remove(group);
        }

        public void DeletePawn(CustomPawn pawn) {
            List<CustomRelationship> toDelete = new List<CustomRelationship>();
            foreach (var r in relationships) {
                if (r.source == pawn || r.target == pawn) {
                    deletionList.Add(r);
                }
            }
            // Remove the pawn from any parent/child group that they are in.  If the parent/child
            // group is empty after their removal, remove that group.
            List<ParentChildGroup> groupsToRemove = new List<ParentChildGroup>();
            foreach (var group in parentChildGroups) {
                int index = group.Parents.IndexOf(pawn);
                if (index != -1) {
                    group.Parents.RemoveAt(index);
                }
                index = group.Children.IndexOf(pawn);
                if (index != -1) {
                    group.Children.RemoveAt(index);
                }
                if (group.Parents.Count == 0 && group.Children.Count == 0) {
                    groupsToRemove.Add(group);
                }
            }
            foreach (var group in groupsToRemove) {
                parentChildGroups.Remove(group);
            }
            parentChildPawns.RemoveAll((CustomPawn p) => { return p == pawn; });
            
            foreach (var r in deletionList) {
                relationships.Remove(r);
            }

            this.parentChildPawns.RemoveAll((CustomPawn p) => {
                return (p == pawn);
            });

            dirty = true;
        }

        public CustomPawn ReplaceNewTemporaryCharacter(int index) {
            var pawn = temporaryPawns[index];
            temporaryPawns[index] = CreateNewTemporaryPawn(pawn.Gender);
            CustomPawn result = AddTemporaryParentChildPawn(pawn);
            return result;
        }

        public CustomPawn CreateNewTemporaryPawn(Gender gender) {
            CustomPawn pawn = new CustomPawn(new Randomizer().GeneratePawn(new PawnGenerationRequestWrapper() {
                FixedGender = gender
            }.Request));
            Faction faction;
            Find.World.factionManager.TryGetRandomNonColonyHumanlikeFaction(out faction, true, true, TechLevel.Undefined);
            if (faction == null) {
                faction = Find.World.factionManager.OfAncients;
            }
            if (faction != null) {
                pawn.Pawn.SetFactionDirect(faction);
            }
            pawn.Type = CustomPawnType.Temporary;
            pawn.Index = NextTemporaryParentChildIndex;
            return pawn;
        }
    }
}

