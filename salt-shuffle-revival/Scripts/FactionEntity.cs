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

		public FactionEntity() {}

		public FactionEntity(string blueprint, int Weight = DEFAULT_WEIGHT) {
			Blueprint = blueprint;
			Name = GameObjectFactory.Factory.GetBlueprintIfExists(Blueprint)?.CachedDisplayNameStripped;
			if (Name.IsNullOrEmpty()) {
				Blueprint = "Dog";
                Name = Blueprint;
			}
			this.Weight = Weight;
			if (FactionTracker.IsHeroic(blueprint)) {
				Properties[nameof(FactionTracker.IsHeroic)] = "true";
            }
            Properties[nameof(Blueprint)] = Blueprint;
        }

		public FactionEntity(GameObject go, bool fromBlueprint) {
			Blueprint = null;

            Name = go.DisplayNameOnlyDirectAndStripped;
            Factions = FactionTracker.GetCreatureFactions(go) ?? new();
			Strength = go.GetStatValue("Strength");
			Agility = go.GetStatValue("Agility");
			Toughness = go.GetStatValue("Toughness");
			Intelligence = go.GetStatValue("Intelligence");
			Willpower = go.GetStatValue("Willpower");
			Ego = go.GetStatValue("Ego");
			Level = go.GetStatValue("Level");
			Tier = go.GetTier();
			IsBaetyl = go.Brain?.GetPrimaryFaction() == "Baetyl";

            a = go.a;
			DetailColor = go.Render.DetailColor;
			FgColor = ColorUtility.StripBackgroundFormatting(go.Render.ColorString);
			FromBlueprint = fromBlueprint;

			try {
				Desc = ColorUtility.StripFormatting(go.GetPart<Description>().GetShortDescription(true, true));
			} catch (Exception) {
				// traipsing mortar was having issues getting description in game init, so we just default to the non-minevented short description
				Desc = ColorUtility.StripFormatting(go.GetPart<Description>()._Short);
			}

			Properties[nameof(go.Blueprint)] = go.Blueprint;

			Properties[nameof(Factions)] = Factions.Aggregate((string)null, (a,n) => a + (!a.IsNullOrEmpty() ? "," : null) + n);

			if (FactionTracker.IsHeroic(go)) {
                Properties[nameof(FactionTracker.IsHeroic)] = "true";
			}

			if (go.HasPart<Lovely>()) { 
				Properties[nameof(Lovely)] = "true";
			}

			Weight = GetWeight(go);

            // if the game object was created explicitly to create this FE, it should be tidied up
            if (FromBlueprint) go.Release();
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
				return new(GameObject.Create(Blueprint, Context: $"Plaidman.SaltShuffleRevival.{nameof(FactionEntity)}"), true);
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
