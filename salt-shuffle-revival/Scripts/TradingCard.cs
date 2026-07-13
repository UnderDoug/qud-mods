using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Plaidman.SaltShuffleRevival;
using Qud.UI;
using XRL.Collections;
using XRL.Rules;

namespace XRL.World.Parts {
	[Serializable]
	public class SSR_Card : IScribedPart, IModEventHandler<SSR_UninstallEvent> {
		public const double BASE_CARD_VALUE = 5.0; // whatever the Plaidman_SSR_Card blueprint's commerce value is set to
		public const double TARGET_PLAYER_COMMERCE = 0.75; // the intended "base" value players should be able to sell cards for
		public const double MERCHANT_COMMERCE_MULTI = 0.67; // modifies how much a merchant over/undervalues cards

		public int SunScore = 0;
		public int MoonScore = 0;
		public int StarScore = 0;
		public int PointValue = 0;
		public string ShortDisplayName = "";
		public bool Foil = false;
		public bool Random;
		public string Faction;
		public string Blueprint;

		// allows storing arbitrary info without changing the class' members; great for modders to extend the mod
		public Dictionary<string, string> Properties = new();

		public override void Read(GameObject basis, SerializationReader reader) {
			if (reader.ModVersions["Plaidman_SaltShuffleRevival"] == new Version("1.0.0")) {
				SunScore = (int)reader.ReadObject();
				MoonScore = (int)reader.ReadObject();
				StarScore = (int)reader.ReadObject();
				PointValue = (int)reader.ReadObject();
				ShortDisplayName = (string)reader.ReadObject();
				Foil = false;
				return;
			}
			base.Read(basis, reader);
		}

		public override void Register(GameObject go, IEventRegistrar registrar) {
			registrar.Register(ObjectCreatedEvent.ID);
			registrar.Register(GetIntrinsicValueEvent.ID);
			registrar.Register(AdjustValueEvent.ID);
			registrar.Register(The.Game, SSR_UninstallEvent.ID);
			registrar.Register(GetDebugInternalsEvent.ID);
			registrar.Register("CanBeDisassembled");
			base.Register(go, registrar);
		}

		private double GetUnmodifiedCommerceValue()
			=> ParentObject != null
			&& ParentObject.GetBlueprint().TryGetPartParameter(nameof(Commerce), nameof(Commerce.Value), out double value)
			&& value != 0
			? value
			: BASE_CARD_VALUE
			;

		private double GetPlayerCommerceValueMultiplier()
			=> TARGET_PLAYER_COMMERCE / GetUnmodifiedCommerceValue()
			;

		// gets the current trader, but only if the trade screen is open/visible
		private static bool TryGetTrader(out GameObject Trader)
			=> (Trader = TradeScreen.Trader) != null
			&& TradeScreen.instance?.Visible is true
			;

		// gets the entity from whose perspective the value is being determined (basically, a trader, if one exists)
		public static GameObject GetInterestedParty(GameObject Card) {
			// if no one is holding the card or there is no one being traded with there's no one interested
			// ensures that general value calculations remain intact
			if (Card.Holder is not GameObject holder || !TryGetTrader(out var trader)) {
				return null;
			}

			// if the holder (who exists) is not the player then the party evaluating the value is the holder (trader who is selling),
			// otherwise the holder is the player so the party who is evaluating is the trader (who is buying)
			return !holder.IsPlayer()
				? holder
				: trader
				;
		}

		public static bool IsSelling(GameObject Trader, GameObject Object)
			=> Trader == Object.Holder
			;

		public static void UnderValueMulti(ref double Value, double? Multi = null)
			=> Value *= (Multi ?? MERCHANT_COMMERCE_MULTI)
			;

		// turns 0.67 into 1.33 which is milder than the 1.49 that (value /= 0.67) results in;
		// then multiplies by that; or simply multiplies by the provided number if it's bigger than 1.0
		public static void OverValueMulti(ref double Value, double? Multi = null) {
			double multi = Multi ?? MERCHANT_COMMERCE_MULTI;
			if (multi < 1.0) multi = 1.0 + (1.0 - multi);
			if (multi < 0) multi = -multi;
			Value *= multi;
		}

		public int GetFeelingLevel(int Feeling)
			=> Feeling switch {
				>= 100 => 2,
				>= 50 => 1,
				> -50 => 0,
				> -100 => -1,
				_ => -2
			};

		// get's the trader's feeling about the factions on a card.
		// takes all the trader's factions and crosses them with the factions on the card, aggregating the feelings toward each one
		public int GetAggregateFeelingLevel(GameObject Trader) {
			if (Trader == null
				|| FactionTracker.GetCreatureFactions(Trader) is not List<string> traderFactions) {
				return GetFeelingLevel(0);
			}

			using var factions = ScopeDisposedList<string>.GetFromPool();
			if (TryGetProperty(nameof(FactionEntity.Factions), out string factionsString)
				&& factionsString.CachedCommaExpansion() is IEnumerable<string> factionsEnumerable
				&& !factionsEnumerable.IsNullOrEmpty()) {
				factions.AddRange(factionsEnumerable);
			} else if (!Faction.IsNullOrEmpty()) {
				factions.Add(Faction);
			}

			if (factions.IsNullOrEmpty()) return GetFeelingLevel(0);

			return traderFactions.Aggregate(
				seed: 0,
				func: delegate (int a, string n) {
					return a + factions.Aggregate(
						seed: a,
						func: delegate (int a2, string n2) {
							var traderFaction = Factions.GetIfExists(n);
							return a2 + GetFeelingLevel(traderFaction.GetFeelingTowardsFaction(n2));
						});
				});
		}

		public static bool CardDepictsInterestedParty(GameObject Trader, GameObject Card)
			=> Trader != null
			&& Card != null
			&& Trader.Blueprint == Card.GetPart<SSR_Card>().Blueprint
			;

		public static bool FactionsOverlap(IEnumerable<string> SourceFactions, IEnumerable<string> OtherFactions)
			=> SourceFactions?.Any(f => OtherFactions?.Contains(f) is true) is true
			;

		public bool IsHeroic()
			=> PropertyIsTrue(nameof(FactionTracker.IsHeroic))
			;

		public override bool HandleEvent(GetIntrinsicValueEvent e) {
			if (e.Object == ParentObject) {
				if (ParentObject.Holder?.IsPlayer() is true) {
					// this reduces a card's value to 0.75 if it's being sold by the player,
					// unless the commerce value has been modified by something else (such as extradimensional or gigantic),
					// then it's proportionally reduced, ie extradimensional still doubles the value to 1.5 from 0.75
					UnderValueMulti(ref e.Value, GetPlayerCommerceValueMultiplier());
				}
				// Non-Heroic cards are worth less? Easy to implement/unimplement
				if (!IsHeroic()) {
					UnderValueMulti(ref e.Value, 0.67);
				}
				if (Foil) OverValueMulti(ref e.Value, 4.0);
			}
			return base.HandleEvent(e);
		}

		public override bool HandleEvent(AdjustValueEvent e) {
			if (e.Object == ParentObject) {
				if (GetInterestedParty(ParentObject) is GameObject interestedParty) {

					Properties["LastTrader"] = interestedParty.BaseDisplayNameStripped;

					using var factions = ScopeDisposedList<string>.GetFromPool();
					if (TryGetProperty(nameof(FactionEntity.Factions), out string factionsString)
						&& factionsString.CachedCommaExpansion() is IEnumerable<string> factionsEnumerable
						&& !factionsEnumerable.IsNullOrEmpty()) {

						Properties["LastTraderFactions"] = factionsString;

						factions.AddRange(factionsEnumerable);
					} else if (!Faction.IsNullOrEmpty()) {
						factions.Add(Faction);
					}
					bool factionOverlap = FactionsOverlap(factions, FactionTracker.GetCreatureFactions(interestedParty));
					if (factionOverlap) {
						if (!CardDepictsInterestedParty(interestedParty, ParentObject)) {
							// makes traders undervalue cards of any of their own factions (they have/see a lot of them)
							// unless it's a card of themselves
							UnderValueMulti(ref e.Value);
							Properties.Remove("LastTraderDepictsSelf");
						} else {
							// makes traders highly overvalue cards of themselves (out of vanity)
							OverValueMulti(ref e.Value, 7.0); // 7.5 results in a max-ego player being able to repeatedly trade back and forth for profit.
							Properties["LastTraderDepictsSelf"] = "Yes";
						}
					}
					int traderFeeling = GetAggregateFeelingLevel(interestedParty);

					Properties["LastTraderFeeling"] = $"{traderFeeling}";

					if (!IsSelling(interestedParty, ParentObject)) {
						// trader wont pay for cards from factions (or combinations thereof) they hate
						if (traderFeeling < -1) {
							UnderValueMulti(ref e.Value, 0.0);
						}
						// trader will overpay for cards from factions (or combinations thereof) they "love" except for their own faction(s),
						// on the basis of how much they love the combination of factions
						if (traderFeeling > 1
							&& !CardDepictsInterestedParty(interestedParty, ParentObject)
							&& !factionOverlap) {
							double loveMulti = Math.Min(3.0, 1.0 + Math.Floor(traderFeeling * 0.5));
							OverValueMulti(ref e.Value, loveMulti);
						}
					}
				}
			}
			return base.HandleEvent(e);
		}

		public bool HandleEvent(SSR_UninstallEvent e) {
			ParentObject.Obliterate("uninstall", true);
			return base.HandleEvent(e);
		}

		public override bool HandleEvent(ObjectCreatedEvent e) {
			if (Random) {
				// if Random is set true in the object blueprint, find a random creature, set it
				SetCreature(FactionTracker.GetRandomCreature());
			} else if (!Blueprint.IsNullOrEmpty()) {
				// if Blueprint is defined in the object blueprint, find the FE for it, set it; fall back to random creature
				if (FactionTracker.RequireCreature(Blueprint) is FactionEntity blueprintFE) {
					SetCreature(blueprintFE);
				} else {
					SetCreature(FactionTracker.GetRandomCreature());
				}
			} else if (!Faction.IsNullOrEmpty()) {
				// if Faction is defined in the object blueprint, find a random FE for it, set it; fall back to random creature
				if (FactionTracker.GetRandomCreature(Faction) is FactionEntity factionFE) {
					SetCreature(factionFE);
				} else {
					SetCreature(FactionTracker.GetRandomCreature());
				}
			}
			// if none of these are the case, then simply exist until SetCreature is called elsewhere
			return base.HandleEvent(e);
		}

		public override bool HandleEvent(GetDebugInternalsEvent e) {
			e.AddEntry(this, nameof(ParentObject), ParentObject.Blueprint);
			e.AddEntry(this, nameof(Foil), Foil);
			e.AddEntry(this, nameof(Random), Random);
			e.AddEntry(this, nameof(Faction), Faction ?? "undefined");
			e.AddEntry(this, nameof(Blueprint), Blueprint ?? "undefined");
			using (var propertyPairs = ScopeDisposedList<string>.GetFromPool()) {
				foreach ((var name, var value) in Properties) {
					if (name.StartsWith("LastTrader")) continue;
					propertyPairs.Add($"{name}: {value}");
				}
				e.AddEntry(this, nameof(Properties), propertyPairs.Aggregate((string)null, (a, n) => a + (!a.IsNullOrEmpty() ? "\n" : null) + n) ?? "empty");
			}
			if (Properties.TryGetValue("LastTrader", out string lastTrader)) {
				using (var lastTraderInfo = ScopeDisposedList<string>.GetFromPool()) {
					lastTraderInfo.Add($"Name: {lastTrader}");
					foreach (var lastTraderProp in Properties.Keys.Where(k => k.StartsWith("LastTrader") && k != "LastTrader")) {
						lastTraderInfo.Add($"{lastTraderProp["LastTrader".Length..]}: {Properties[lastTraderProp]}");
					}
					e.AddEntry(this, "Last Trader", lastTraderInfo.Aggregate((string)null, (a, n) => a + (!a.IsNullOrEmpty() ? "\n" : null) + n));
				}
			}
			return base.HandleEvent(e);
		}

		// stops gigantic and extradimensional mods (or any others) from making trading cards disassemblable
		public override bool FireEvent(Event E)
		{
			if (E.ID == "CanBeDisassembled")
				return false;

			return base.FireEvent(E);
		}

		// forces no stacking
		public override bool SameAs(IPart p)
			=> false
			;

		// opening a starter deck
		public static GameObject CreateCard(Random Rnd = null) {
			var card = GameObject.Create("Plaidman_SSR_Card", Context: "Plaidman.SaltShuffleRevival.StarterDeck");
			var part = card.GetPart<SSR_Card>();
			Rnd ??= card.GetSeededRandom($"Plaidman.SaltShuffleRevival.{nameof(CreateCard)}");
			part.SetCreature(FactionTracker.GetRandomCreature(Rnd: Rnd));
			return card;
		}

		// opening a booster and generate a deck for an opponent
		public static GameObject CreateCard(string faction, Random Rnd = null) {
			var card = GameObject.Create("Plaidman_SSR_Card", Context: $"Plaidman.SaltShuffleRevival.Booster::{faction}");
			var part = card.GetPart<SSR_Card>();
			Rnd ??= card.GetSeededRandom($"Plaidman.SaltShuffleRevival.{nameof(CreateCard)}.{faction}");
			part.SetCreature(FactionTracker.GetRandomCreature(faction, Rnd: Rnd));
			return card;
		}

		// when the opponent is bested in card combat
		public static GameObject CreateCard(GameObject go) {
			var card = GameObject.Create("Plaidman_SSR_Card", Context: $"Plaidman.SaltShuffleRevival.Victory::{go.BaseID}");
			var part = card.GetPart<SSR_Card>();
			part.SetCreature(FactionEntity.GetFromGameObject(go, false));
			return card;
		}

		private void SetCreature(FactionEntity fe) {
			var rnd = ParentObject.GetSeededRandom($"Plaidman.SaltShuffleRevival.{nameof(SSR_Card)}.{nameof(SetCreature)}");
			fe ??= FactionTracker.GetRandomCreature(Rnd: rnd);

			float sunScore = 2;
			float moonScore = 2;
			float starScore = 2;

			int xpLevel = Math.Max(5, fe.Level);
			sunScore += fe.Strength;
			moonScore += fe.Agility;
			sunScore += fe.Toughness;
			moonScore += fe.Intelligence;
			starScore += fe.Willpower;
			starScore += fe.Ego;
			float minScore = new float[]{ sunScore, moonScore, starScore }.Min();

			sunScore -= minScore * 2 / 3;
			moonScore -= minScore * 2 / 3;
			starScore -= minScore * 2 / 3;
			float total = sunScore + moonScore + starScore;

			SunScore = (int) Math.Round(sunScore * xpLevel / total);
			MoonScore = (int) Math.Round(moonScore * xpLevel / total);
			StarScore = (int) Math.Round(starScore * xpLevel / total);

			int error = xpLevel - (SunScore + MoonScore + StarScore);
			SunScore += error;

			Foil = rnd.Next(10) == 0;

			NonBlueprintVariance(fe, rnd);
			BoostLowLevel(rnd);
			BoostFoil(rnd);

			if (fe.IsBaetyl) {
				SunScore = -5;
				MoonScore = -5;
				StarScore = -5;
			}

			PointValue = SunScore + MoonScore + StarScore;

			Faction = fe.Factions.FirstOrDefault(s => !s.IsNullOrEmpty());
			Blueprint = fe.GetProperty(nameof(GameObject.Blueprint));

			SetColors(fe);
			SetDescription(fe);
			SetDisplayName(fe);

			Properties = new(fe.Properties);

			if (fe.PropertyIsTrue(nameof(Lovely)))
				ParentObject.RequirePart<Lovely>();
			else
				ParentObject.RemovePart<Lovely>();
		}

		private void NonBlueprintVariance(FactionEntity fe, Random Rnd = null) {
			if (fe.FromBlueprint) return;

			// blueprint entities have some natural variance in their dice rolls
			// FEs that are generated from GOs are set in stone, so we artificially add some variance
			// adjust each stat by 2d3 - 2 => -2,-1,-1,0,0,0,1,1,2
			// if that would reduce the stat to zero or less, just use the old stat

			Rnd ??= Stat.Rnd2;

			var oldMoon = MoonScore;
			MoonScore += Rnd.Next(3) + Rnd.Next(3) - 2;
			if (MoonScore < 1) MoonScore = oldMoon;

			var oldStar = StarScore;
			StarScore += Rnd.Next(3) + Rnd.Next(3) - 2;
			if (StarScore < 1) StarScore = oldStar;

			var oldSun = SunScore;
			SunScore += Rnd.Next(3) + Rnd.Next(3) - 2;
			if (SunScore < 1) SunScore = oldSun;
		}

		// make low level cards more interesting by boosting a couple stats
		// some get 3 or 4 points in a single stat
		// some get 4 then 2, and some get 3 then 2
		private void BoostLowLevel(Random Rnd = null) {
			const int LowLevel = 8;
			if (MoonScore + StarScore + SunScore >= LowLevel) return;

			Rnd ??= Stat.Rnd2;

			var times = Rnd.Next(2) + Rnd.Next(2); // 2d2-2 = distribution 0,1,1,2
			var boost = Rnd.Next(2) + 3; // start with 3 or 4 point boost
			for (int i = 0; i < times; i++) {
				var stat = Rnd.Next(3);
				BoostStat(stat, boost);

				// second loop will always boost a stat by 2
				boost = 2;
			}
		}

		private void BoostFoil(Random Rnd = null) {
			if (!Foil) return;

			Rnd ??= Stat.Rnd2;

			var first = Rnd.Next(3);
			var second = Rnd.Next(2);
			if (second == first) {
				second = 2; // 0 => 2/1, 1 => 0/2, 2 => 0/1
			}

			BoostStat(first, 2);
			BoostStat(second, 1);
		}

		private void BoostStat(int stat, int boost) {
			switch (stat) {
				case 0: MoonScore += boost; break;
				case 1: SunScore += boost; break;
				case 2: StarScore += boost; break;
			}
		}

		private void SetColors(FactionEntity fe) {
			ParentObject.Render.ColorString = fe.FgColor;
			ParentObject.Render.DetailColor = fe.DetailColor;
		}

		private void SetDescription(FactionEntity fe) {
			var builder = new StringBuilder();

			if (Foil) {
				builder.Append("A {{Y|reflective}} trading card with an animated illustration of =a==name= plus various cryptic statistics. The card {{Y|shimmers}} when viewed at different angles.\n\n");
			} else {
				builder.Append("A trading card with a stylized illustration of =a==name= plus various cryptic statistics.\n\n");
			}

			var factions = fe.Factions?.Select(s => Factions.Get(s).DisplayName.Capitalize())?.ToList();
			if (factions.Count > 0) {
				builder.Append("{{G|Allegiance: =factions=}}\n");
			}

			builder.Append("{{W|Sun:}} {{Y|=sun=}}\xff\xff\xff{{C|Moon:}} {{Y|=moon=}}\xff\xff\xff{{M|Star:}} {{Y|=star=}}\n\n{{K|=desc=}}");

			builder.StartReplace()
				.AddReplacer("a", fe.a)
                .AddReplacer("name", "{{|" + fe.Name + "}}")
                .AddReplacer("factions", string.Join(", ", factions))
				.AddReplacer("sun", SunScore.ToString())
				.AddReplacer("moon", MoonScore.ToString())
				.AddReplacer("star", StarScore.ToString())
				.AddReplacer("desc", fe.Desc)
				.Execute();

			ParentObject.GetPart<Description>().Short = builder.ToString();
		}

		private void SetDisplayName(FactionEntity fe) {
			var builder = new StringBuilder("=name==foil= {{W|=sun=}}/{{C|=moon=}}/{{M|=star=}}");
			builder.StartReplace()
				.AddReplacer("name", "{{|" + fe.Name + "}}")
                .AddReplacer("foil", Foil ? " ({{Y|F}})" : null)
                .AddReplacer("sun", SunScore.ToString())
				.AddReplacer("moon", MoonScore.ToString())
				.AddReplacer("star", StarScore.ToString())
				.Execute();
			ShortDisplayName = builder.ToString();

			builder.Append(" {{K|(Lv =lv==foil=)}}");
			builder.StartReplace()
				.AddReplacer("lv", PointValue.ToString())
				.AddReplacer("foil", "")
				.Execute();
			ParentObject.DisplayName = builder.ToString();
		}

        public bool HasProperty(string Name)
            => !Name.IsNullOrEmpty()
            && Properties.ContainsKey(Name)
            ;

        public string GetProperty(string Name, string Default = null)
            => Name.IsNullOrEmpty() || !Properties.TryGetValue(Name, out string value)
            ? Default
            : value
            ;

        public bool TryGetProperty(string Name, out string Value)
            => (Value = GetProperty(Name)) != null
            ;

        public bool PropertyIsTrue(string Name)
            => TryGetProperty(Name, out var value)
			&& value.EqualsNoCase("true")
            ;

		// this makes it so that for cards depicting "heroic" creatures, their tile gets used but only in the "Look UI", nowhere else
        public override bool Render(RenderEvent E)
        {
            if (E.Context == "Look,Tooltip"
                && IsHeroic()
                && TryGetProperty("HeroicIcon", out string heroicIcon)
				&& !heroicIcon.IsNullOrEmpty()) {
				E.Tile = heroicIcon;
            }
            return base.Render(E);
        }
	}
}
