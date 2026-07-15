using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Plaidman.SaltShuffleRevival;
using Qud.UI;
using XRL.Collections;
using XRL.Rules;
using SerializeField = UnityEngine.SerializeField;

namespace XRL.World.Parts {
	[Serializable]
	public class SSR_Card : IScribedPart, IModEventHandler<SSR_UninstallEvent>
	{
		public enum CardScore : int
		{
			Sun,
			Moon,
			Star,
		}

		public const double BASE_CARD_VALUE = 5.0; // whatever the Plaidman_SSR_Card blueprint's commerce value is set to
		public const double TARGET_PLAYER_COMMERCE = 0.75; // the intended "base" value players should be able to sell cards for
		public const double MERCHANT_COMMERCE_MULTI = 0.67; // modifies how much a merchant over/undervalues cards

        public const string BEFORE_SET_CREATURE_EVENT_ID = "Plaidman_SaltShuffleRevival_BeforeSetCreature";
        public const string SET_CREATURE_EVENT_ID = "Plaidman_SaltShuffleRevival_SetCreature";

        private static readonly Event BeforeSetCreatureEvent = new(BEFORE_SET_CREATURE_EVENT_ID);
        private static readonly Event SetCreatureEvent = new(SET_CREATURE_EVENT_ID);

        public int? SunScore = null;
		public int? MoonScore = null;
		public int? StarScore = null;

		public int Level => GetSunScore() + GetMoonScore() + GetStarScore();
		
		public bool? Foil;
		public bool IsFoil
		{
			get => ParentObject.HasPart<Mod_SSR_Foil>();
			set
			{
				if (IsFoil != value)
				{
					if (value)
						ParentObject.ApplyModification(new Mod_SSR_Foil());
					else
						ParentObject.RemovePart<Mod_SSR_Foil>();
                }
			}
        }
		public bool Random;
		public string Faction;
		public string Blueprint;

		[SerializeField]
		private FactionEntity _FactionEntity;
		public FactionEntity FactionEntity
		{
			get => _FactionEntity;
			protected set => _FactionEntity = value;
        }

		// allows storing arbitrary info without changing the class' members; great for modders to extend the mod
		public Dictionary<string, string> Properties => FactionEntity?.Properties;

		// no longer used
		public string ShortDisplayName = null;
		public int? PointValue = null;

		public SSR_Card()
			: base()
		{ }

		public override void Read(GameObject basis, SerializationReader reader) {
			if (reader.ModVersions["Plaidman_SaltShuffleRevival"] == new Version("1.0.0")) {
				SunScore = (int)reader.ReadObject();
				MoonScore = (int)reader.ReadObject();
				StarScore = (int)reader.ReadObject();
				PointValue = (int)reader.ReadObject();
				ShortDisplayName = (string)reader.ReadObject();
				return;
			}
			base.Read(basis, reader);
		}

		private bool MatchesFactionEntity(FactionEntity FactionEntity)
		{
			if ((FactionEntity?.Factions).IsNullOrEmpty())
				return false;

			if (!Faction.IsNullOrEmpty())
				if (!FactionEntity.Factions.Contains(Faction))
					return false;

			if (GetProperty(nameof(FactionEntity.Factions), "").CachedCommaExpansion() is IEnumerable<string> factions)
				if (!factions.Any(faction => FactionEntity.Factions.Contains(faction)))

			if (!Blueprint.IsNullOrEmpty())
				if (FactionEntity.GetProperty(nameof(Blueprint)) != Blueprint)
					return false;

			return true;
		}

        public override void FinalizeRead(SerializationReader Reader)
        {
            base.FinalizeRead(Reader);
			if (Foil.HasValue)
				IsFoil = Foil.GetValueOrDefault();

			if (FactionEntity == null)
			{
				string context = GetContext(nameof(FinalizeRead));
				var rnd = ParentObject.GetSeededRandom(context);
				var entity = FactionTracker.GetAllFactionEntities(MatchesFactionEntity).GetRandomElement(rnd).GetCreature();
                SetCreature(
					fe: entity,
					Rnd: rnd,
					Context: context);
            }
        }

		public override void Register(GameObject go, IEventRegistrar registrar)
		{
			registrar.Register("CanBeDisassembled");

            registrar.Register(The.Game, SSR_UninstallEvent.ID);

            base.Register(go, registrar);
		}

        public override bool WantEvent(int ID, int cascade)
            => base.WantEvent(ID, cascade)
            || ID == ObjectCreatedEvent.ID
            || ID == GetIntrinsicValueEvent.ID
            || ID == AdjustValueEvent.ID
            || ID == GetDisplayNameEvent.ID
            || ID == GetShortDescriptionEvent.ID
            || ID == GetDebugInternalsEvent.ID
            ;

        public override IPart DeepCopy(GameObject Parent, Func<GameObject, GameObject> MapInv) {
            var card = base.DeepCopy(Parent, MapInv) as SSR_Card;
			card.FactionEntity = FactionEntity?.Clone();
			return card;
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
			=> Trader != null && Trader == Object?.Holder
			;

		public bool IsSelling(GameObject Trader)
			=> IsSelling(Trader, ParentObject)
			;

		public static bool IsBuying(GameObject Trader, GameObject Object)
			=> Trader != null && !IsSelling(Trader, Object)
			;

		public bool IsBuying(GameObject Trader)
			=> IsBuying(Trader, ParentObject)
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

		public bool DepictsInterestedParty(GameObject Trader)
			=> CardDepictsInterestedParty(Trader, ParentObject)
			;

		public static bool FactionsOverlap(IEnumerable<string> SourceFactions, IEnumerable<string> OtherFactions)
			=> SourceFactions?.Any(f => OtherFactions?.Contains(f) is true) is true
			;

		public int GetSunScore()
			=> SunScore ?? 0
			;

		public int GetMoonScore()
			=> MoonScore ?? 0
			;

		public int GetStarScore()
			=> StarScore ?? 0
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
				int condition = ParentObject.hitpoints;
				if (condition > 0) {
					switch (condition) {
						case 10: OverValueMulti(ref e.Value, 2.0);
							break;
						case >= 7: UnderValueMulti(ref e.Value, condition / 5.0);
                            break;
						case >= 4: UnderValueMulti(ref e.Value, condition / 10.0);
							break;
						default: UnderValueMulti(ref e.Value, condition / 25.0);
							break;
					}
				}
			}
			return base.HandleEvent(e);
		}

		public override bool HandleEvent(AdjustValueEvent e) {
			if (e.Object == ParentObject) {
				if (GetInterestedParty(ParentObject) is GameObject interestedParty) {

                    SetProperty("LastTrader", interestedParty.BaseDisplayNameStripped);

					using var factions = ScopeDisposedList<string>.GetFromPool();
					if (TryGetProperty(nameof(FactionEntity.Factions), out string factionsString)
						&& factionsString.CachedCommaExpansion() is IEnumerable<string> factionsEnumerable
						&& !factionsEnumerable.IsNullOrEmpty()) {

                        SetProperty("LastTraderFactions", factionsString);

						factions.AddRange(factionsEnumerable);
					} else if (!Faction.IsNullOrEmpty()) {
						factions.Add(Faction);
					}
					bool cardDepictsTrader = DepictsInterestedParty(interestedParty);
                    bool factionOverlap = FactionsOverlap(factions, FactionTracker.GetCreatureFactions(interestedParty));
					if (factionOverlap) {
						if (!cardDepictsTrader) {
							// makes traders undervalue cards of any of their own factions (they have/see a lot of them)
							// unless it's a card of themselves
							UnderValueMulti(ref e.Value);
							RemoveProperty("LastTraderDepictsSelf");
						} else {
							double vanityMulti = 7.0; // 7.5 results in a max-ego player being able to repeatedly trade back and forth for profit
                            if (IsBuying(interestedParty)) {
								vanityMulti = 4.0; // not quite as high if player selling
							}
							// makes traders highly overvalue cards of themselves (out of vanity)
							OverValueMulti(ref e.Value, vanityMulti);
                            SetProperty("LastTraderDepictsSelf", "Yes");
						}
					}
					int traderFeeling = GetAggregateFeelingLevel(interestedParty);

					SetProperty("LastTraderFeeling", $"{traderFeeling}");

					if (IsBuying(interestedParty)) {
						// trader wont pay for cards from factions (or combinations thereof) they hate
						if (traderFeeling < -1) {
							UnderValueMulti(ref e.Value, 0.0);
						}
						// trader will overpay for cards from factions (or combinations thereof) they "love" except for their own faction(s),
						// on the basis of how much they love the combination of factions
						if (traderFeeling > 1
							&& !cardDepictsTrader
                            && !factionOverlap) {
							double loveMulti = Math.Min(3.0, 1.0 + Math.Floor(traderFeeling * 0.5));
							OverValueMulti(ref e.Value, loveMulti);
						}
					}
				}
			}
			return base.HandleEvent(e);
		}

		public override bool HandleEvent(GetDisplayNameEvent e) {
			e.AddClause(GetDisplayNameStats());
			e.AddTag(GetDisplayNameLevel());
			return base.HandleEvent(e);
		}

		public override bool HandleEvent(GetShortDescriptionEvent e) {
			AppendCardDescription(e.Infix);
			AppendAllegianceDescription(GetProperty(nameof(FactionEntity.Factions)).CachedCommaExpansion(), e.Infix);
			AppendStatsDescription(e.Infix);
			AppendEntityDescription(FactionEntity, e.Infix);
			return base.HandleEvent(e);
		}

		public virtual bool HandleEvent(SSR_UninstallEvent e) {
			ParentObject.Obliterate("uninstall", true);
			return base.HandleEvent(e);
		}

		public override bool HandleEvent(ObjectCreatedEvent e) {
			_ = ParentObject.BaseID;
			string seed = GetContext(nameof(SSR_Card));
			string context = e.Context?.Replace("Plaidman.SaltShuffleRevival.", "");
            if (!context.IsNullOrEmpty()) {
				seed = $"{seed}::{context}";
			} else {
				context = null;
			}
			var rnd = ParentObject.GetSeededRandom(seed);
			FactionEntity entity = null;
			if (Random) {
				// if Random is set true in the object blueprint, find a random creature
				entity = FactionTracker.GetRandomCreature(Rnd: rnd);
			} else if (!Blueprint.IsNullOrEmpty()) {
				// if Blueprint is defined in the object blueprint, find the FE for it; fall back to random creature
				if (FactionTracker.RequireCreature(Blueprint) is FactionEntity blueprintFE) {
                    entity = blueprintFE;
				} else {
                    entity = FactionTracker.GetRandomCreature(Rnd: rnd);
				}
			} else if (!Faction.IsNullOrEmpty()) {
				Faction = FactionTracker.ClosestFaction(Faction);
				// if Faction is defined in the object blueprint, find a random FE for it; fall back to random creature
				if (FactionTracker.GetRandomCreature(Faction, Rnd: rnd) is FactionEntity factionFE) {
                    entity = factionFE;
				} else {
                    entity = FactionTracker.GetRandomCreature(Rnd: rnd);
				}
			}
			if (entity != null) SetCreature(entity, Rnd: rnd, Context: context ?? nameof(ObjectCreatedEvent));
            // if none of these are the case, then simply exist until SetCreature is called elsewhere
            return base.HandleEvent(e);
		}

		public override bool HandleEvent(GetDebugInternalsEvent e)
		{
			e.AddEntry(this, nameof(ParentObject), ParentObject.Blueprint);
			e.AddEntry(this, nameof(Foil), Foil?.ToString() ?? "undefined");
			e.AddEntry(this, nameof(IsFoil), IsFoil);
			e.AddEntry(this, nameof(Random), Random);
			e.AddEntry(this, nameof(Faction), Faction ?? "undefined");
			e.AddEntry(this, nameof(Blueprint), Blueprint ?? "undefined");

			using (var propertyPairs = ScopeDisposedList<string>.GetFromPool())
			{
				foreach ((var name, var value) in Properties ?? Enumerable.Empty<KeyValuePair<string, string>>())
				{
					if (name.StartsWith("LastTrader"))
						continue;

					propertyPairs.Add($"{name}: {value}");
				}
				string propertiesString = propertyPairs.Aggregate(
						seed: (string)null,
						func: (a, n) => a + (!a.IsNullOrEmpty() ? "\n" : null) + n)
					?? "empty";
                e.AddEntry(this, nameof(Properties), propertiesString);
			}

			if (Properties != null
				&& Properties.TryGetValue("LastTrader", out string lastTrader))
			{
				using (var lastTraderInfo = ScopeDisposedList<string>.GetFromPool())
				{
					lastTraderInfo.Add($"Name: {lastTrader}");

					foreach (var lastTraderProp in Properties.Keys.Where(k => k.StartsWith("LastTrader") && k != "LastTrader"))
						lastTraderInfo.Add($"{lastTraderProp["LastTrader".Length..]}: {GetProperty(lastTraderProp)}");

					string lastTraderString = lastTraderInfo.Aggregate(
							seed: (string)null,
							func: (a, n) => a + (!a.IsNullOrEmpty() ? "\n" : null) + n)
						?? "empty";

                    e.AddEntry(this, "Last Trader", lastTraderString);
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

		public static string GetContext(string Context = null)
		{
			string seed = $"Plaidman.SaltShuffleRevival";

			if (!Context.IsNullOrEmpty())
				seed = $"{seed}.{Context}";

			return seed;
		}

		public static string GetCreateCardSeed(string Context = null)
		{
			string seed = GetContext(nameof(CreateCard));

			if (!Context.IsNullOrEmpty())
				seed = $"{seed}.{Context}";

			return seed;
		}

		// opening a starter deck
		public static GameObject CreateCard(
			Random Rnd = null,
			double FoilChance = 10.0
			)
		{
			string context = "StarterDeck";

			var card = GameObject.Create("Plaidman_SSR_Card", Context: GetContext(context));
			var part = card.RequirePart<SSR_Card>();

			Rnd ??= card.GetSeededRandom(GetCreateCardSeed(context));

			part.SetCreature(
				fe: FactionTracker.GetRandomCreature(Rnd: Rnd),
                Rnd: Rnd,
                FoilChance: FoilChance,
                UndamagedChance: 25,
                MaxDamage: 3,
                Context: context);

			return card;
		}

		// opening a booster and generate a deck for an opponent
		public static GameObject CreateCard(
			string faction,
			Random Rnd = null,
			double FoilChance = 10.0
			)
		{
			string context = $"Booster::{faction}";

            var card = GameObject.Create("Plaidman_SSR_Card", Context: GetContext(context));
			var part = card.RequirePart<SSR_Card>();

			Rnd ??= card.GetSeededRandom(GetCreateCardSeed(context));

			part.SetCreature(
				fe: FactionTracker.GetRandomCreature(faction, Rnd: Rnd),
                Rnd: Rnd,
                FoilChance: FoilChance,
                UndamagedChance: 10,
                MaxDamage: 3,
                Context: context);

			return card;
		}

		// when the opponent is bested in card combat
		public static GameObject CreateCard(
			GameObject go,
			double FoilChance = 10.0
			)
		{
			string context = $"Victory::{go.Blueprint}:{go.BaseID}";

            var card = GameObject.Create("Plaidman_SSR_Card", Context: GetContext(context));
			var part = card.RequirePart<SSR_Card>();

			part.SetCreature(
				fe: FactionEntity.GetFromGameObject(go, false),
				FoilChance: FoilChance,
				UndamagedChance: 10,
				MaxDamage: 3, 
				Context: context);

			return card;
		}

		public static int CalculateScore(float Score, int XPLevel, float Total)
			=> (int)Math.Round(Score * XPLevel / Total)
			;

        public bool CalculateBaseScores(FactionEntity fe = null)
		{
			fe ??= FactionEntity;

			if (fe == null)
				return false;

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

			if (SunScore != null)
				sunScore = SunScore.GetValueOrDefault();

			if (MoonScore != null)
                moonScore = MoonScore.GetValueOrDefault();

			if (StarScore != null)
                starScore = StarScore.GetValueOrDefault();

            float minScore = new float[] { sunScore, moonScore, starScore }.Min();

            sunScore -= minScore * 2 / 3;
            moonScore -= minScore * 2 / 3;
            starScore -= minScore * 2 / 3;
            float total = sunScore + moonScore + starScore;

			bool predefinedScores = SunScore != null
				|| MoonScore != null
				|| StarScore != null;

			AdjustScore(CardScore.Sun, CalculateScore(sunScore, xpLevel, total));
			AdjustScore(CardScore.Moon, CalculateScore(moonScore, xpLevel, total));
			AdjustScore(CardScore.Star, CalculateScore(starScore, xpLevel, total));

            int error = xpLevel - Level;
			if (error != 0
				&& !predefinedScores)
				AdjustScore(CardScore.Sun, error);

			return true;
        }

		private bool SetCreature(
			FactionEntity fe,
			Random Rnd = null,
			double FoilChance = 10.0,
			int UndamagedChance = 1,
			int? MinDamage = null,
			int? MaxDamage = null,
			string Context = null
			)
		{
			string seed = GetContext($"{nameof(SSR_Card)}.{nameof(SetCreature)}");

			if (!Context.IsNullOrEmpty())
				seed = $"{seed}.{Context}";

			Rnd ??= ParentObject.GetSeededRandom(seed);
			fe ??= FactionTracker.GetRandomCreature(Rnd: Rnd);

            if (ParentObject.HasRegisteredEvent(BeforeSetCreatureEvent.ID))
			{
                BeforeSetCreatureEvent.Clear();
                BeforeSetCreatureEvent
                    .AddParameter("Card", ParentObject)
                    .AddParameter(nameof(FactionEntity), fe)
                    .AddParameter(nameof(FoilChance), FoilChance)
                    .AddParameter(nameof(Context), Context);

				try
				{
                    if (!ParentObject.FireEvent(BeforeSetCreatureEvent))
						return false;

                    fe = BeforeSetCreatureEvent.GetParameter<FactionEntity>(nameof(FactionEntity));
                    FoilChance = BeforeSetCreatureEvent.GetParameter<double>(nameof(FoilChance));
                }
				finally
				{ 
					BeforeSetCreatureEvent.Clear();
				}
            }

            FactionEntity = fe.Clone();

            SetColors();
            SetDisplayName();
            ClearShortDescription();
            CalculateBaseScores();

			bool doFoil = false;
			if (!Foil.HasValue)
                doFoil = Rnd.Next(10000) < (int)(FoilChance * 100.0);
			else
                doFoil = Foil.GetValueOrDefault();

			if (doFoil)
                ParentObject.ApplyModification(new Mod_SSR_Foil(), Creation: true);

            NonBlueprintVariance(FactionEntity, Rnd);
			BoostLowLevel(Rnd);

			BoostFoil(Rnd);

			if (FactionEntity.IsBaetyl)
			{
				SunScore = -5;
				MoonScore = -5;
				StarScore = -5;
			}

			Faction = FactionEntity.Factions.FirstOrDefault(s => !s.IsNullOrEmpty()) ?? "Dogs";
			Blueprint = FactionEntity.GetProperty(nameof(GameObject.Blueprint));

			if (PropertyIsTrue(nameof(Lovely)))
				ParentObject.RequirePart<Lovely>();
			else
				ParentObject.RemovePart<Lovely>();

			if (Rnd.Next(100) >= UndamagedChance)
			{
				int damageLow = MinDamage ?? 2;
				int damageHigh = (MaxDamage ?? 9) + 1;
				int damage = Rnd.Next(damageLow, damageHigh);

				if (damage > 0)
					ParentObject.TakeDamage(ref damage, Attributes: "Plaidman_SSR_CardCondition");
			}

            if (ParentObject.HasRegisteredEvent(SetCreatureEvent.ID))
			{
                SetCreatureEvent.Clear();
                SetCreatureEvent
                    .AddParameter("Card", ParentObject)
                    .AddParameter(nameof(FactionEntity), fe)
                    .AddParameter(nameof(FoilChance), FoilChance)
                    .AddParameter(nameof(Context), Context);

				ParentObject.FireEvent(SetCreatureEvent);
                SetCreatureEvent.Clear();
            }
			return true;
		}

		private void NonBlueprintVariance(FactionEntity fe = null, Random Rnd = null) {
			fe ??= FactionEntity;

			if (fe.FromBlueprint) return;

            // blueprint entities have some natural variance in their dice rolls
            // FEs that are generated from GOs are set in stone, so we artificially add some variance
            // adjust each stat by 2d3 - 2 => -2,-1,-1,0,0,0,1,1,2
            // if that would reduce the stat to zero or less, just use the old stat

			// skip if the entity is a named creature;
			// the card shouldn't have variable stats if they are.
            if (fe.PropertyIsTrue(nameof(FactionTracker.IsHeroic))) return;

            Rnd ??= Stat.Rnd2;

			var oldMoon = GetMoonScore();
			MoonScore += Rnd.Next(3) + Rnd.Next(3) - 2;
			if (GetMoonScore() < 1) MoonScore = oldMoon;

			var oldStar = GetStarScore();
			StarScore += Rnd.Next(3) + Rnd.Next(3) - 2;
			if (GetStarScore() < 1) StarScore = oldStar;

			var oldSun = GetSunScore();
			SunScore += Rnd.Next(3) + Rnd.Next(3) - 2;
			if (GetSunScore() < 1) SunScore = oldSun;
		}

		// make low level cards more interesting by boosting a couple stats
		// some get 3 or 4 points in a single stat
		// some get 4 then 2, and some get 3 then 2
		private void BoostLowLevel(Random Rnd = null)
		{
			const int LowLevel = 8;

			if (Level >= LowLevel)
				return;

			Rnd ??= Stat.Rnd2;

			var times = Rnd.Next(2) + Rnd.Next(2); // 2d2-2 = distribution 0,1,1,2
			var boost = Rnd.Next(2) + 3; // start with 3 or 4 point boost
			for (int i = 0; i < times; i++)
			{
				var score = Rnd.Next(3);
				AdjustScore((CardScore)score, boost);

				// second loop will always boost a stat by 2
				boost = 2;
			}
		}

		private void BoostFoil(Random Rnd = null)
		{
			if (!IsFoil) return;

			Rnd ??= Stat.Rnd2;

			var first = Rnd.Next(3);
			var second = Rnd.Next(2);

			if (second == first)
				second = 2; // 0 => 2/1, 1 => 0/2, 2 => 0/1

			AdjustScore((CardScore)first, 2);
			AdjustScore((CardScore)second, 1);
		}

		private int AdjustScore(CardScore Score, int boost)
			=> Score switch
			{
				CardScore.Sun => (SunScore += boost).GetValueOrDefault(),
				CardScore.Moon => (MoonScore += boost).GetValueOrDefault(),
				CardScore.Star => (StarScore += boost).GetValueOrDefault(),
				_ => 0,
			}
			;

		private void SetColors(FactionEntity fe = null)
		{
			fe ??= FactionEntity;

			if (fe == null)
				return;

			ParentObject.Render.ColorString = fe.FgColor;
			ParentObject.Render.TileColor = fe.FgColor;
			ParentObject.Render.DetailColor = fe.DetailColor;
		}

		public StringBuilder AppendCardDescription(StringBuilder SB)
		{
			SB ??= Event.NewStringBuilder();
			return IsFoil
				? SB.Append("A {{Y|reflective}} trading card with an animated illustration of =a==name= plus various cryptic statistics. The card {{Y|shimmers}} when viewed at different angles.")
				: SB.Append("A trading card with a stylized illustration of =a==name= plus various cryptic statistics.");
		}

		public StringBuilder AppendAllegianceDescription(IEnumerable<string> Factions, StringBuilder SB)
		{
            SB ??= Event.NewStringBuilder();

			var factions = Factions
					?.Select(s => World.Factions.GetIfExists(s)?.DisplayName?.Capitalize())
					?.Where(s => !s.IsNullOrEmpty())
				?? Enumerable.Empty<string>();

			if (factions.Count() > 0)
			{
				if (!SB.IsNullOrEmpty())
                    SB.AppendLine().AppendLine();

				SB.Append("{{G|Allegiance: ")
					.Append(factions.Aggregate((string)null, (a, n) => a + (!a.IsNullOrEmpty() ? "; " : null) + n));
			}
			return SB;
        }

		public StringBuilder AppendStatsDescription(StringBuilder SB)
		{
			SB ??= Event.NewStringBuilder();

			if (!SB.IsNullOrEmpty())
                SB.AppendLine().AppendLine();

            return SB
				.Append("{{W|Sun:}} {{Y|=sun=}}").Append("\xff\xff\xff")
				.Append("{{C|Moon:}} {{Y|=moon=}}").Append("\xff\xff\xff")
				.Append("{{M|Star:}} {{Y|=star=}}")
				;
        }

		public StringBuilder AppendEntityDescription(FactionEntity fe, StringBuilder SB)
			=> (SB ?? Event.NewStringBuilder()).Append(fe.Desc)
			;

		private void ClearShortDescription() 
			=> ParentObject.GetPart<Description>().Short = null
			;

		public string GetDisplayNameStats()
			=> "{{W|=sun=}}/{{C|=moon=}}/{{M|=star=}}"
				.StartReplace()
				.AddReplacer("sun", GetSunScore().ToString())
				.AddReplacer("moon", GetMoonScore().ToString())
				.AddReplacer("star", GetStarScore().ToString())
				.ToString()
			;

		public string GetDisplayNameLevel()
			=> "{{K|(Lv =lv=)}}"
                .StartReplace()
                .AddReplacer("lv", Level.ToString())
                .ToString()
			;

        private void SetDisplayName(FactionEntity fe = null)
		{
			fe ??= FactionEntity;

			if (fe == null)
				return;

            ParentObject.DisplayName = "{{|" + fe.Name + "}}";
        }

		public string SetProperty(string Name, string Value, bool RemoveIfNull = false)
		{
			if (Properties == null)
				return null;

			Properties[Name] = Value;

			if (RemoveIfNull
				&& Value == null)
			{
                RemoveProperty(Name);
				return null;
			}
			return Properties[Name];
		}

		public bool RemoveProperty(string Name)
			=> Name != null
			&& Properties?.Remove(Name) is true
			;

        public bool HasProperty(string Name)
            => !Name.IsNullOrEmpty()
            && Properties?.ContainsKey(Name) is true
            ;

        public string GetProperty(string Name, string Default = null)
            => Name.IsNullOrEmpty()
				|| Properties?.GetValue(Name) is not string value
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
                //&& IsHeroic()
                && TryGetProperty("HeroicIcon", out string heroicIcon)
				&& !heroicIcon.IsNullOrEmpty())
				E.Tile = heroicIcon;

            return base.Render(E);
        }
	}
}
