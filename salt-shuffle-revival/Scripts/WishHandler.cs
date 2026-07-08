using System.Collections.Generic;
using System.Linq;
using Plaidman.SaltShuffleRevival;
using XRL.UI;
using XRL.World;
using XRL.World.Parts;

namespace XRL.Wish {
	[HasWishCommand]
	class SSR_Wishes {
		[WishCommand(Command = "ssr")]
		public void HandleWish(string more) {
			var split = more.Split(' ');

			switch (split[0].ToLower()) {
				case "booster":
					if (split.Length == 1 || split[1].Length == 0)
						ParseFaction(FactionTracker.GetRandomFaction());
					else ParseFaction(split[1]);
					break;

				case "starter":
					The.Player.TakeObject(GameObjectFactory.Factory.CreateObject("Plaidman_SSR_Starter"));
					break;

				case "box":
					The.Player.TakeObject(GameObjectFactory.Factory.CreateObject("Plaidman_SSR_BoosterBox"));
					break;

				case "debug":
                    HandleDebugWish();
					break;

				default:
					Popup.Show("SSR: invalid object type. Use 'booster', 'starter', or 'box'\n\nUse 'debug' for debug output");
					break;
			}
		}

		private void ParseFaction(string faction) {
			if (faction.ToLower() == "box") {
				The.Player.TakeObject(GameObjectFactory.Factory.CreateObject("Plaidman_SSR_BoosterBox"));
				return;
			}

			var closest = FactionTracker.ClosestFaction(faction);
			var go = GameObjectFactory.Factory.CreateObject("Plaidman_SSR_Booster");
			go.GetPart<SSR_BoosterPack>().OverrideFaction(closest);
			The.Player.TakeObject(go);
		}

		[WishCommand(Command = "debug ssr")]
		public void HandleDebugWish() {
			var sB = Event.NewStringBuilder("GameID: {").Append(The.Game?.GameID ?? "NO_GAME").Append("}")
				.AppendLine().Append("FactionTracker ID: {").Append(FactionTracker.GetID() ?? "NO_TRACKER_ID").Append("}")
				.AppendLine();
			if (FactionTracker.GetInstance() is FactionTracker instance) {
				if (The.Game?.GetSystem<FactionTracker>() is not FactionTracker addedFactionTracker
					|| addedFactionTracker != instance) {
                    sB.AppendLine().Append("The FactionTracker appears to be disconnected from The.Game's systems.");
                }
				sB.AppendLine().Append("Factions Members (").Append(instance.FactionMemberCache?.Values?.Aggregate(0, (a, n) => a + (n?.Count ?? 0)) ?? 0).Append("):");
				foreach ((var faction, var factionMembers) in instance.FactionMemberCache ?? Enumerable.Empty<KeyValuePair<string, List<FactionEntity>>>()) {
					sB.AppendLine()
						.Append(faction).Append(" (").Append(factionMembers?.Count ?? 0).Append("):");
					foreach (var factionMember in factionMembers) {
						sB.AppendLine()
							.Append("\xff\xff\xff:\xff").Append(factionMember.Name);
						if (factionMember.FromBlueprint || (factionMember.Name.StartsWith("[") && factionMember.Name.EndsWith("]"))) {
							sB.Append(" (").Append(factionMember.Blueprint).Append(")");
						}
						sB.Append("; [").Append(factionMember.Weight).Append("]");
						if (factionMember.GetProperty(nameof(FactionTracker.IsHeroic), "false").EqualsNoCase("true"))
							sB.Append(" ").Append(nameof(FactionTracker.IsHeroic));
					}
				}
			} else {
				sB.AppendLine().Append("The FactionTracker appears to be unintialized.");
			}
			string output = Event.FinalizeString(sB);
			UnityEngine.Debug.Log(output.Replace("\xff", " "));
			Popup.Show(output);
		}
	}
}