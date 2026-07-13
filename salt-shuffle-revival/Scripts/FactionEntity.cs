using System;
using System.Collections.Generic;
using System.Linq;
using ConsoleLib.Console;
using XRL.World;
using XRL.World.Parts;

namespace Plaidman.SaltShuffleRevival {
	[Serializable]
	public class FactionEntity : IComposite, IDisposable {
		public const int DEFAULT_WEIGHT = 5;
		public const int HEROIC_WEIGHT = 1;
		public const string GET_FROM_GO_EVENT_ID = "Plaidman_SaltShuffleRevival_GetFEFromGO";
		private static readonly Event GetFEFromGOEvent = new(GET_FROM_GO_EVENT_ID);

        public readonly string Blueprint;
		public bool FromBlueprint;
		public string Name;

		public List<string> Factions;
		public bool IsBaetyl;
		public int Tier;

		public int Strength;
		public int Agility;
		public int Toughness;
		public int Intelligence;
		public int Ego;
		public int Willpower;
		public int Level;

		public string a;
		public string DetailColor;
		public string FgColor;
		public string Desc;

		public int Weight = DEFAULT_WEIGHT;

		public Dictionary<string, string> Properties = new();

		public bool WantFieldReflection => false;
		public void Write(SerializationWriter writer) { writer.WriteNamedFields(this, GetType()); }
		public void Read(SerializationReader reader) { reader.ReadNamedFields(this, GetType()); }

		public FactionEntity() {
			Blueprint = null;
		}

		public FactionEntity(string blueprint, int Weight = DEFAULT_WEIGHT) {
			Blueprint = blueprint;
			Name = GameObjectFactory.Factory.GetBlueprintIfExists(Blueprint)?.DisplayName();
			if (Name.IsNullOrEmpty()) {
				Blueprint = "Dog";
                Name = Blueprint.ToLower();
			}
            if (!Options.EnableCardNameColors) {
                Name = Name.Strip();
			}
            this.Weight = Weight;
			if (FactionTracker.IsHeroic(blueprint)) {
				Properties[nameof(FactionTracker.IsHeroic)] = "true";
            }
            Properties[nameof(Blueprint)] = Blueprint;
        }

		protected FactionEntity(FactionEntity fe) {
			Blueprint = fe.Blueprint;

			Name = fe.Name;
			Factions = new(fe.Factions);

			Strength = fe.Strength;
			Agility = fe.Agility;
			Toughness = fe.Toughness;
			Intelligence = fe.Intelligence;
			Ego = fe.Ego;
			Willpower = fe.Willpower;

			Level = fe.Level;
			Tier = fe.Tier;

			IsBaetyl = fe.IsBaetyl;
            Properties = new(fe.Properties);

			a = fe.a;
			DetailColor = fe.DetailColor;
			FgColor = fe.FgColor;
			FromBlueprint = fe.FromBlueprint;

			Desc = fe.Desc;

			Weight = fe.Weight;
		}

		public static FactionEntity GetFromGameObject(GameObject go, bool fromBlueprint) {
			var fe = new FactionEntity();

            if (Options.EnableCardLongNames) {
                fe.Name = go.GetDisplayName(
					Context: $"",
					AsIfKnown: true,
					Single: true,
					NoConfusion: true,
					Visible: true,
					Short: true,
					Reference: true);
			} else {
                fe.Name = go.DisplayNameOnlyDirect;
			}

            if (!Options.EnableCardNameColors)
                fe.Name = fe.Name.Strip();

            fe.Factions = FactionTracker.GetCreatureFactions(go);
            fe.Strength = go.GetStatValue("Strength");
            fe.Agility = go.GetStatValue("Agility");
            fe.Toughness = go.GetStatValue("Toughness");
            fe.Intelligence = go.GetStatValue("Intelligence");
            fe.Willpower = go.GetStatValue("Willpower");
            fe.Ego = go.GetStatValue("Ego");
            fe.Level = go.GetStatValue("Level");
            fe.Tier = go.GetTier();
            fe.IsBaetyl = go.Brain?.GetPrimaryFaction() == "Baetyl";

            fe.a = go.a;
            string fgColor = null;
            try {
                var entityRender = go.RenderForUI(AsIfKnown: true);
                fe.DetailColor = entityRender.GetDetailColorChar().ToString();
                // Gets the foreground colour whether it's been defined as the tile color or as a color string
                fgColor = entityRender.GetForegroundColorChar().ToString();
            } catch {
                var entityRender = go.Render;
                fe.DetailColor = entityRender.getDetailColor().ToString();
                // Gets the foreground colour whether it's been defined as the tile color or as a color string
                fgColor = entityRender.GetForegroundColorChar().ToString();
            }
            // stops cards from being only a single color (in order to improve the card's visuals)
            if (fgColor == fe.DetailColor) {
                // switches the fg shade; if it was dark, make it bright, if it was bright make it dark
                fgColor = fgColor.ToLower() == fgColor ? fgColor.ToUpper() : fgColor.ToLower();
                // don't use dark black
                if (fgColor == "k") fgColor = "y";
            }
            fe.FgColor = $"&{fgColor}";

            fe.FromBlueprint = fromBlueprint;

            try {
                fe.Desc = ColorUtility.StripFormatting(go.GetPart<Description>().GetShortDescription(true, true));
            } catch (Exception) {
                // traipsing mortar was having issues getting description in game init, so we just default to the non-minevented short description
                fe.Desc = ColorUtility.StripFormatting(go.GetPart<Description>()._Short);
            }

            fe.Properties[nameof(go.Blueprint)] = go.Blueprint;

            fe.Properties[nameof(Factions)] = fe.Factions.Aggregate((string)null, (a, n) => a + (!a.IsNullOrEmpty() ? "," : null) + n);

            if (FactionTracker.IsHeroic(go)) {
                fe.Properties[nameof(FactionTracker.IsHeroic)] = "true";
                fe.Properties["HeroicIcon"] = go.GetTile();
            }

            if (go.HasPart<Lovely>()) {
                fe.Properties[nameof(Lovely)] = "true";
            }

            fe.Weight = GetWeight(go);

			try {
                if (go.HasRegisteredEvent(GetFEFromGOEvent.ID)) {
                    GetFEFromGOEvent.Clear();
                    GetFEFromGOEvent
                        .AddParameter("Object", go)
                        .AddParameter(nameof(FactionEntity), fe);

					if (!go.FireEvent(GetFEFromGOEvent))
						return null;

                    fe = GetFEFromGOEvent.GetParameter<FactionEntity>(nameof(FactionEntity));
					GetFEFromGOEvent.Clear();
                }
                return fe;
            } finally {
                // if the game object was created explicitly to create this FE, it should be tidied up
                if (fe.FromBlueprint) go.Release();
            }
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

        public static int GetWeight(GameObjectBlueprint Blueprint)
            => !FactionTracker.IsHeroic(Blueprint)
            ? DEFAULT_WEIGHT
            : HEROIC_WEIGHT
            ;

        public static int GetWeight(GameObject Object)
            => !FactionTracker.IsHeroic(Object)
            ? DEFAULT_WEIGHT
            : HEROIC_WEIGHT
            ;

        public FactionEntity GetCreature() {
			if (Blueprint != null) {
				// create a new FE based on a GO so we can take advantage of BP dice rolls for stats
				return GetFromGameObject(
					go: GameObject.Create(Blueprint, Context: $"Plaidman.SaltShuffleRevival.{nameof(FactionEntity)}"),
					fromBlueprint: true);
			}
			return this;
		}

		public static FactionEntity GetCreature(string blueprint, int Weight = DEFAULT_WEIGHT) {
			using var blueprintFE = new FactionEntity(blueprint, Weight);
			return blueprintFE.GetCreature();
		}

		public FactionEntity Clone() {
			return new FactionEntity(this);
		}

		public bool Equals(FactionEntity other) {
			return Name == other.Name && Tier == other.Tier;
		}

		public void Dispose() {
			Name = null;
			Factions = null;

			Strength = 0;
			Agility = 0;
			Toughness = 0;
			Intelligence = 0;
			Ego = 0;
			Willpower = 0;

			Level = 0;
			Tier = 0;

            IsBaetyl = false;
            Properties = null;

			a = null;
			DetailColor = null;
			FgColor = null;
			FromBlueprint = false;

			Desc = null;
		}
	}
}
