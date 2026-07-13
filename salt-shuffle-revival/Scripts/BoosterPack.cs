using System;
using System.Text;
using ConsoleLib.Console;
using Plaidman.SaltShuffleRevival;
using XRL.UI;

namespace XRL.World.Parts {
	[Serializable]
	public class SSR_BoosterPack : IScribedPart, IModEventHandler<SSR_UninstallEvent> {
		public string Faction;
		public bool Starter = false;
		public double FoilChance = 10.0;

		public override void Read(GameObject basis, SerializationReader reader) {
			if (reader.ModVersions["Plaidman_SaltShuffleRevival"] == new Version("1.0.0")) {
				Faction = (reader.ReadObject() as Faction)?.Name ?? null;
				Starter = reader.ReadBoolean();
				return;
			}
			base.Read(basis, reader);
		}

		public override void Register(GameObject go, IEventRegistrar registrar) {
			registrar.Register(GetIntrinsicValueEvent.ID);
			registrar.Register(AdjustValueEvent.ID);
			registrar.Register(GetInventoryActionsEvent.ID);
			registrar.Register(InventoryActionEvent.ID);
			registrar.Register(ObjectCreatedEvent.ID);
			registrar.Register(The.Game, SSR_UninstallEvent.ID);

			base.Register(go, registrar);
        }

        public override bool HandleEvent(GetIntrinsicValueEvent e) {
            if (e.Object == ParentObject) {
				if (e.Object.Holder?.IsPlayer() is true) {
					if (Starter) {
						// starters should be functionally valueless for players trying to sell
						SSR_Card.UnderValueMulti(ref e.Value, 0.001);
					} else {
						// boosters are more akin to regular loot, but still relatively valueless to sell
						SSR_Card.UnderValueMulti(ref e.Value, 0.01);
					}
				}
            }
            return base.HandleEvent(e);
        }

		public override bool HandleEvent(AdjustValueEvent e) {
			if (e.Object == ParentObject && !Starter) {
				if (SSR_Card.GetInterestedParty(ParentObject) is GameObject interestedParty) {
					if (!Faction.IsNullOrEmpty() && FactionTracker.GetCreatureFactions(interestedParty).Contains(Faction)) {
                        SSR_Card.UnderValueMulti(ref e.Value);
                    }
				}
            }
            return base.HandleEvent(e);
		}

        public bool HandleEvent(SSR_UninstallEvent e) {
			ParentObject.Count = 1;
			ParentObject.Obliterate("uninstall", true);
			return base.HandleEvent(e);
		}

		public override bool HandleEvent(ObjectCreatedEvent e) {
			_ = ParentObject.BaseID; // forces the ID to be generated at the point of object creation.
			if (Starter) {
				Faction = null;
			} else {
                // this allows for object blueprints that inherit from Plaidman_SSR_Booster to specify a faction
                if (Faction.IsNullOrEmpty()) {
					Faction = FactionTracker.GetRandomFaction(ParentObject.GetSeededRandom("Plaidman.SaltShuffleRevival.Booster")) ?? "Dogs";
				} else {
					Faction = FactionTracker.ClosestFaction(Faction);
				}
                OverrideFaction(Faction);
            }
			return base.HandleEvent(e);
        }

		// forces no stacking
		public override bool SameAs(IPart p)
			=> false
			;

        public void OverrideFaction(string faction) {
			var entry = Factions.GetIfExists(faction) ?? Factions.GetIfExists("Dogs");
			Faction = entry?.Name;
			if (entry != null) {
				ParentObject.DisplayName = $"pack of Salt Shuffle cards: {entry?.DisplayName}";
				if (entry.Emblem is FactionEmblem emblem) {
					var renderable = ParentObject.RenderForUI(AsIfKnown: true);
					string existingColor = renderable.GetForegroundColorChar().ToString();

					string detailColor = emblem.DetailColor.ToString();
					if (detailColor == existingColor) {
						if (!emblem.getTileColor().IsNullOrEmpty()) {
							detailColor = emblem.getTileColor().Replace("&", "");
						} else {
							detailColor = ColorUtility.StripBackgroundFormatting(emblem.ColorString).Replace("&", "");
						}
						if (detailColor == existingColor) {
							detailColor = detailColor.ToLower() == detailColor ? detailColor.ToUpper() : detailColor.ToLower();
						}
					}

					if (!detailColor.IsNullOrEmpty()) {
						ParentObject.Render.DetailColor = detailColor;
					}
				}
			}
		}

		public override bool HandleEvent(GetInventoryActionsEvent e) {
			e.AddAction(
				Name: "Unwrap",
				Key: 'o',
				FireOnActor: false,
				Display: "open",
				Command: "InvCommandUnwrap",
				Default: 2
			);

			return base.HandleEvent(e);
		}

		public override bool HandleEvent(InventoryActionEvent e) {
			if (e.Command != "InvCommandUnwrap") return base.HandleEvent(e);

			if (!Starter && !FactionTracker.FactionHasMembers(Faction)) {
				Messages.MessageQueue.AddPlayerMessage("You opened the pack but it was empty. Weird.");
				ParentObject.Destroy("Unwrapped", true);
				return base.HandleEvent(e);
			}

			var tally = new StringBuilder("You unwrap the =pack= and get:\n");

			var additionalCards = Event.NewGameObjectList();
			var allCards = Event.NewGameObjectList();

			var rnd = ParentObject.GetSeededRandom($"Plaidman.SaltShuffleRevival.InvCommandUnwrap");

			GameObject firstCard = null;
			var qty = Starter ? 12 : 5;
			for (int i = 0; i < qty; i++) {
				var card = Starter
					? SSR_Card.CreateCard(rnd, FoilChance)
					: SSR_Card.CreateCard(Faction, rnd, FoilChance);

				if (i > 0)
					firstCard = card;
				else
					additionalCards.Add(card);

				allCards.Add(card);

				e.Actor.TakeObject(card, NoStack: true);
			}

			if (firstCard != null) {
				try {
					WasDerivedFromEvent.Send(ParentObject, ParentObject, firstCard, additionalCards, allCards, "InvCommandUnwrap");
				} catch (Exception x) {
					MetricsManager.LogError(x);
				}

				try {
					foreach (var card in allCards)
						DerivationCreatedEvent.Send(card, ParentObject, ParentObject, "InvCommandUnwrap");
				} catch (Exception x) {
					MetricsManager.LogError(x);
				}
			}
			
			foreach (var card in allCards)
				tally.Append("- {{|").Append(card.DisplayName).Append("}}\n");

			tally.StartReplace()
				.AddReplacer("pack", ParentObject.DisplayName)
				.Execute();

			Popup.Show(Message: tally.ToString(), LogMessage: false);
			ParentObject.Destroy("Unwrapped", true);

			return base.HandleEvent(e);
		}
	}
}
