using System;
using System.Collections.Generic;
using System.Linq;
using Qud.API;
using XRL;
using XRL.Collections;
using XRL.Language;
using XRL.Rules;
using XRL.UI;
using XRL.World;
using XRL.World.Parts;

namespace Plaidman.SaltShuffleRevival {
    [HasGameBasedStaticCache]
    [HasCallAfterGameLoaded]
    [Serializable]
	public class FactionTracker : IPlayerSystem {
        [GameBasedStaticCache(CreateInstance = false)]
        private static FactionTracker Instance;
		public const string UninstallCommand = "Plaidman_SaltShuffleRevival_Command_Uninstall";

		private string GameID;

        public Dictionary<string, List<FactionEntity>> FactionMemberCache;

		public override bool WantFieldReflection => false;
		public override void Write(SerializationWriter writer) {
			writer.WriteNamedFields(this, GetType());
			writer.WriteOptimized(GameID);
		}

		public override void Read(SerializationReader reader) {
			reader.ReadNamedFields(this, GetType());
			GameID = reader.ReadOptimizedString();
		}

        private static FactionTracker InitializeSystem() => new() { GameID = The.Game?.GameID };

        [CallAfterGameLoaded]
        [GameBasedCacheInit]
        public static void FactionTrackerSystemInit() {
            if (Instance == null) {
                Instance = The.Game?.RequireSystem(InitializeSystem);
                if (Instance != null && Instance.GameID == null) {
                    Instance.GameID = The.Game.GameID;
				}
            } else if (Instance.GameID != null && Instance.GameID != The.Game?.GameID) {
                Instance = null;
                FactionTrackerSystemInit();
                return;
            } else if (The.Game?.GetSystem<FactionTracker>() == null) {
                The.Game?.AddSystem(Instance);
			}

            if (Instance != null) {
                Loading.LoadTask($"Printing Cards", Instance.TrackFactions);
			} else if (The.Game != null) {
                ModManager.GetMod().Error($"Failed to load {nameof(FactionTracker)}.");
			}
        }

		public static string GetID()
			=> GetInstance()?.GameID
			;

        private static bool CheckFactionEntity(FactionEntity entity, string baseShortDesc) {
			// skip entities that haven't been assigned a non-default a display name ("[Object]", "[Creature]")
			if (entity.Name.StartsWith("[")
				&& entity.Name.EndsWith("]")) {
				entity.Dispose();
				return false;
			}

			// skip entities that haven't been assigned a non-default description
			if (entity.Desc?.StartsWith(baseShortDesc) is true) {
				entity.Dispose();
				return false;
			}
			return true;
		}

        public void TrackFactions() {
            GameID ??= The.Game.GameID;

			if (!FactionMemberCache.IsNullOrEmpty())
				return;

			var physicalObjectBlueprint = GameObjectFactory.Factory.GetBlueprintIfExists("PhysicalObject");
            string baseShortDesc = physicalObjectBlueprint?.GetPartParameter<string>(nameof(Description), nameof(Description.Short))
                ?? "A hideous specimen.";

            var factionList = Factions.GetList().Where(f => f.Visible && !f.GetMembers(Dynamic: false).IsNullOrEmpty());

			Event.PinCurrentPool(); // there's gonna be a lot of events thrown around here
			try {
				foreach (var faction in factionList) {
					Event.ResetPool();
					var factionMembers = faction.GetMembers(Dynamic: false)
						.Where(IsEligibleForFactionEntity)
						.Select(bp => new FactionEntity(bp.Name, FactionEntity.GetWeight(bp)))
						.ToList();

					// removes any faction entities that would result in creatures with missing display names or descriptions.
					// there are Pariah creatures and BaseSightless that both show up.
					// the former requires entering a cell to generate, the latter is lacking the BaseObject tag (probably a bug/oversight).
					factionMembers.RemoveAll(delegate (FactionEntity fe) {
						if (fe.GetCreature() is not FactionEntity entity) {
                            fe.Dispose();
							return true;
						}
						try {
							if (!CheckFactionEntity(entity, baseShortDesc)) {
								fe.Dispose();
								return true;
							}
							return false;
						} finally {
							entity.Dispose();
                        }
					});

                    if (!FactionMemberCache.TryGetValue(faction.Name, out List<FactionEntity> existingFactionMembers)) {
						FactionMemberCache.Add(faction.Name, factionMembers);
                    } else {
						foreach (var factionMember in factionMembers) {
							existingFactionMembers.AddIfNot(factionMember, existingFactionMembers.Contains);
                        }
                        FactionMemberCache[faction.Name] = existingFactionMembers;
                    }
				}

				// check the zone object cache for entities to add
				if (The.ZoneManager?.CachedObjects is Dictionary<string, GameObject> cachedObjects) {
					var cachedObjectFactionEntities = cachedObjects.Values
						.Where(go => GetCreatureFactions(go).Count > 0 && IsEligibleForFactionEntity(go.GetBlueprint()))
						.Select(go => FactionEntity.GetFromGameObject(go, false));

					foreach (var entity in cachedObjectFactionEntities) {
						Event.ResetPool();

						if (!CheckFactionEntity(entity, baseShortDesc))
							continue;

                        foreach (var faction in entity.Factions) {
							if (!FactionMemberCache.TryGetValue(faction, out List<FactionEntity> factionMembers)) {
								factionMembers = new();
								FactionMemberCache.Add(faction, factionMembers);
                            }
                            // remove this game object's blueprint entry and replace it with their actual entry, if they're cached
                            if (entity.PropertyIsTrue(nameof(IsHeroic))) {
								factionMembers.RemoveAll(delegate (FactionEntity member) {
									if (!member.Equals(entity)) {
										var blueprint = member.Blueprint ?? member.Properties?.GetValue(nameof(FactionEntity.Blueprint));
										var otherBlueprint = entity?.Blueprint ?? entity.Properties?.GetValue(nameof(FactionEntity.Blueprint));

										if (blueprint != null && blueprint == otherBlueprint)
											return member.Blueprint != null;
                                    }
                                    return  false;
								});
                            }
							if (factionMembers.Any(member => member.Equals(entity))) continue;
							factionMembers.Add(entity);
						}
					}
				}
			} finally { 
				Event.ResetToPin();
			}
        }

        public static FactionTracker GetInstance() {
			if (Instance == null) FactionTrackerSystemInit();
            return Instance;
		}

		public FactionTracker() { FactionMemberCache = new(); }

		public static bool IsEligibleForFactionEntity(GameObjectBlueprint Blueprint) {
			// Exclude specific end-game blueprints
			if (Blueprint.Name == "Ehalcodon"
				|| Blueprint.Name == "Spoken Ionic"
				|| Blueprint.Name == "Sheyd"
				|| Blueprint.Name == "Fool of the Gyre"
				|| Blueprint.Name == "Mover Baetyl") {
				return false;
			}

			// Exclude any prologue blueprints 
			if (Blueprint.Name.Contains("Chiliad")
				|| Blueprint.HasTag("SemanticChiliad")) {
				return false;
			}

			// Exclude BaseSightless which is not tagged as a base object (catch anything else like this)
            if (Blueprint.Name.StartsWith("Base"))
				return false;

			// Exclude special creatures
			if (Blueprint.Name.EndsWith(" Cherub") // cherubs need to be spawned with their Spawner blueprint
				|| Blueprint.HasTag("Golem")) { // golems, for obvious reasons
				return false;
			}

            // Exclude uninitialized Sheba Hagadias, unfortunate, but her creature isn't deterministically chosen
            if (Blueprint.HasTag("IsLibrarian"))
				return false;

			return true;
        }

		public static bool IsHeroic(GameObjectBlueprint Blueprint) {
            // this is the metric the game uses
			if (Blueprint.HasPart(nameof(GivesRep)))
                return true;

			// we want a wider net cast
			if (Blueprint.HasProperName())
				return true;

			// if it's not legendary eligible it's probably already legendary
			return !EncountersAPI.IsLegendaryEligible(Blueprint);
        }

		public static bool IsHeroic(string Blueprint) {
			if (Blueprint.IsNullOrEmpty())
				return false;

			if (GameObjectFactory.Factory.GetBlueprintIfExists(Blueprint) is not GameObjectBlueprint model)
				return false;

			return IsHeroic(model);
		}

		public static bool IsHeroic(GameObject Object) {
            if (Object.HasPart(nameof(GivesRep)))
                return true;

			if (Object.HasPropertyOrTag("Hero"))
				return true;

			if (Object.HasProperName)
				return true;

			return IsHeroic(Object.GetBlueprint());
        }

        public override void Register(XRLGame game, IEventRegistrar registrar) {
			registrar.Register(AfterZoneBuiltEvent.ID);
			base.Register(game, registrar);
		}

		public override void RegisterPlayer(GameObject player, IEventRegistrar registrar) {
			registrar.Register(CommandEvent.ID);
			base.RegisterPlayer(player, registrar);
		}

		public override bool HandleEvent(AfterZoneBuiltEvent e) {
			var creatures = e.Zone.GetObjects(go => GetCreatureFactions(go).Count > 0);
			foreach (var creature in creatures) {
				AddFactionMember(creature);
			}
			return base.HandleEvent(e);
		}

		public override bool HandleEvent(CommandEvent e) {
			if (e.Command == UninstallCommand) {
				UninstallParts();
			}
			return base.HandleEvent(e);
		}

		public void UninstallParts() {
			if (!Confirm.ShowNoYes("Are you sure you want to uninstall {{W|Salt Shuffle Revival}}? All cards and booster packs will be removed.")) {
				XRL.Messages.MessageQueue.AddPlayerMessage("{{W|Salt Shuffle Revival}} uninstall was cancelled.");
				return;
			}

			The.Game.HandleEvent(new SSR_UninstallEvent());
			The.Game.RemoveSystem(this);

			Popup.Show("Finished removing {{W|Salt Shuffle Revival}}. Please save and quit, then you can remove this mod.");
		}

		public static bool FactionHasMembers(string faction) {
			return GetInstance().FactionMemberCache.TryGetValue(faction, out List<FactionEntity> factionMembers)
				&& factionMembers.Count > 0;
		}

		private static List<FactionEntity> GetFactionMembers(string faction) {
			var instance = GetInstance();

			if (instance.FactionMemberCache.TryGetValue(faction, out List<FactionEntity> factionMembers)) {
				return factionMembers;
			}

			factionMembers = new();
			instance.FactionMemberCache.Add(faction, factionMembers);
			return factionMembers;
		}

		private static void AddFactionMember(GameObject go) {
			if (go.GetBlueprint().IsBaseBlueprint()) {
				return;
			}

			var entity = FactionEntity.GetFromGameObject(go, false);
			foreach (var faction in entity.Factions) {
				var factionMembers = GetFactionMembers(faction);
				// remove this game object's blueprint entry and replace it with their actual entry, once they're encountered
				if (IsHeroic(go)) factionMembers.RemoveAll(delegate (FactionEntity member) {
                    if (!member.Equals(entity))
                    {
                        var blueprint = member.Blueprint ?? member.Properties?.GetValue(nameof(FactionEntity.Blueprint));
                        var otherBlueprint = entity?.Blueprint ?? entity.Properties?.GetValue(nameof(FactionEntity.Blueprint));

                        if (blueprint != null && blueprint == otherBlueprint)
                            return member.Blueprint != null;
                    }
                    return false;
                });
				if (factionMembers.Any(member => member.Equals(entity))) continue;
				factionMembers.Add(entity);
			}
		}

		private static IEnumerable<string> GetNonEmptyFactions() {
			return GetInstance().FactionMemberCache
				.Where(kvp => kvp.Value.Count > 0)
				.Select(kvp => kvp.Key);
		}

		public static string ClosestFaction(string faction) {
			var keys = GetNonEmptyFactions();
			var factionToLower = faction.ToLower();
			var closest = "";
			var min = int.MaxValue;

			foreach (var key in keys) {
				var keyToLower = key.ToLower();
				if (keyToLower.StartsWith("villagers of ")) {
					keyToLower = keyToLower[13..];
				}

				var dist = Grammar.LevenshteinDistance(factionToLower, keyToLower, false);
				if (dist >= min) continue;

				closest = key;
				min = dist;
			}

			return closest;
		}

		public static string GetRandomFaction(Random Rnd = null) {
			Rnd ??= Stat.Rnd2;
			return GetNonEmptyFactions().GetRandomElement(Rnd);
		}

		// changes here were after it became apparent that the FactionMemberCache was overfilling with legendary creatures;
		// this way, they're weighted towards generic creatures instead
		public static FactionEntity GetRandomCreature(string faction = null, Random Rnd = null) {
            Rnd ??= Stat.Rnd2;

			// Joppa is made invisible later in the world-gen process if you do a non-Joppa start.
			// This stops Irudad/Nima/Yrame from popping up in those games.
			if (faction == null) {
				var instance = GetInstance();
				do {
					faction = GetRandomFaction(Rnd) ?? "Dogs";
					if (Factions.Get(faction)?.Visible is false) {
						instance.FactionMemberCache.Remove(faction);
						faction = null;
					}
                } while (faction == null);
			}
			
			faction ??= "Dogs"; // really early history generation sometimes tries to generate a random card before everything is set up

            // creates a "weighted list" where "heroic" creatures are weighted lower
            var cardBag = new Dictionary<FactionEntity, int>();
			foreach (var fe in GetFactionMembers(faction)) {
				cardBag[fe] = fe.Weight;
            }

			// really early history generation sometimes tries to generate a random card before everything is set up
			if (cardBag.IsNullOrEmpty()) {
				cardBag.Add(new FactionEntity("Dog"), FactionEntity.DEFAULT_WEIGHT);
			}

			// draws a random weighted creature
            return cardBag.GetRandomElement(Rnd).GetCreature();
		}

		public static FactionEntity RequireCreature(string blueprint) {
			if (GameObjectFactory.Factory.GetBlueprintIfExists(blueprint) is not GameObjectBlueprint model)
				return null;

			var instance = GetInstance();

			// sample, disposes itself at the end of scope
            using var sampleEntity = FactionEntity.GetCreature(model.Name, FactionEntity.GetWeight(model));

			foreach (var feList in instance.FactionMemberCache.Values) {
				if (feList.FirstOrDefault(fe => fe.Equals(sampleEntity)) is FactionEntity existingEntity) {
					return existingEntity.GetCreature();
				}
			}

            var entity = sampleEntity.Clone();
            foreach (var faction in entity.Factions) {
                var factionMembers = GetFactionMembers(faction);
                if (factionMembers.Any(member => member.Equals(entity))) continue;
                factionMembers.Add(entity);
            }
            return entity.GetCreature();
		}

		private static bool IsLevelAndFactionVisible(KeyValuePair<string, int> kvp, Brain.AllegianceLevel Level)
			=> Brain.GetAllegianceLevel(kvp.Value) == Level
            && Factions.GetIfExists(kvp.Key)?.Visible is true
			;

		private static bool IsMemberAndFactionVisible(KeyValuePair<string, int> kvp)
			=> IsLevelAndFactionVisible(kvp, Brain.AllegianceLevel.Member)
			;

		private static bool IsAffiliatedAndFactionVisible(KeyValuePair<string, int> kvp)
            => IsLevelAndFactionVisible(kvp, Brain.AllegianceLevel.Affiliated)
            ;

		private static bool IsAssociatedAndFactionVisible(KeyValuePair<string, int> kvp)
            => IsLevelAndFactionVisible(kvp, Brain.AllegianceLevel.Associated)
            ;

        public static List<string> GetCreatureFactions(GameObject go) {
			if (go.Brain == null) return new();

			using var allFactions = ScopeDisposedList<string>.GetFromPool();

			allFactions.AddRange(go.Brain.Allegiance
                .Where(IsMemberAndFactionVisible)
                .Select(kvp => kvp.Key));

            // this also captures entities like Santalalotze who is only associated with Consortium and Chavvah
            allFactions.AddRange(go.Brain.Allegiance
				.Where(IsAffiliatedAndFactionVisible)
				.Select(kvp => kvp.Key));

            // doing it in segments like this ensures that the order is based on the highest allegiance level
            allFactions.AddRange(go.Brain.Allegiance
				.Where(IsAssociatedAndFactionVisible)
				.Select(kvp => kvp.Key));

			// some creatures are ostensibly members of a faction (Blue Jel -> Oozes-25), and will appear in their booster packs,
			// but are more highly associated with another faction (Blue Jel -> Prey-100), meaning (before) you could pull them from a pack
			// and the card wouldn't list the faction they were just pulled from. This addresses that.
			return allFactions.Aggregate(
				seed: new List<string>(),
				func: delegate (List<string> acc, string next)
				{
					// only keep the first three, might get out of hand, otherwise
					if (acc.Count < 3)
						acc.Add(next);
                    return acc;
                });
		}
	}
}
