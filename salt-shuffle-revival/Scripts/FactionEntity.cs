using System;
using System.Collections.Generic;
using ConsoleLib.Console;
using XRL.World;
using XRL.World.Parts;

namespace Plaidman.SaltShuffleRevival {
	[Serializable]
	public class FactionEntity : IComposite, IDisposable {
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
		
		public bool IsLovely;

		public bool WantFieldReflection => false;
		public void Write(SerializationWriter writer) { writer.WriteNamedFields(this, GetType()); }
		public void Read(SerializationReader reader) { reader.ReadNamedFields(this, GetType()); }

		public FactionEntity() {}

		public FactionEntity(string blueprint) {
			Blueprint = blueprint;
			Name = GameObjectFactory.Factory.GetBlueprint(blueprint).DisplayName();
			
			if (!Options.EnableCardNameColors)
				Name = Name.Strip();
		}

		public FactionEntity(GameObject go, bool fromBlueprint) {
			Blueprint = null;

			if (Options.EnableCardLongNames)
				Name = go.GetDisplayName(AsIfKnown: true, Single: true, NoConfusion: true, Short: true);
			else
				Name = go.DisplayNameOnlyDirect;
			
			if (!Options.EnableCardNameColors)
				Name = Name.Strip();
			
			Factions = FactionTracker.GetCreatureFactions(go);
			Strength = go.GetStatValue("Strength");
			Agility = go.GetStatValue("Agility");
			Toughness = go.GetStatValue("Toughness");
			Intelligence = go.GetStatValue("Intelligence");
			Ego = go.GetStatValue("Ego");
			Willpower = go.GetStatValue("Willpower");
			Level = go.GetStatValue("Level");
			Tier = go.GetTier();
			IsBaetyl = go.Brain?.GetPrimaryFaction() == "Baetyl";
			IsLovely = go.HasPart<Lovely>();
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

			// if the game object was created explicitly to create this FE, it should be tidied up
			if (FromBlueprint) go.Release();
		}

		protected FactionEntity(FactionEntity fe) {
			Blueprint = fe.Blueprint;

			Name = fe.Name;
			Factions = fe.Factions;

			Strength = fe.Strength;
			Agility = fe.Agility;
			Toughness = fe.Toughness;
			Intelligence = fe.Intelligence;
			Ego = fe.Ego;
			Willpower = fe.Willpower;

			Level = fe.Level;
			Tier = fe.Tier;

			IsBaetyl = fe.IsBaetyl;
			IsLovely = fe.IsLovely;

			a = fe.a;
			DetailColor = fe.DetailColor;
			FgColor = fe.FgColor;
			FromBlueprint = fe.FromBlueprint;

			Desc = fe.Desc;
		}

		public FactionEntity GetCreature() {
			if (Blueprint != null) {
				// create a new FE based on a GO so we can take advantage of BP dice rolls for stats
				return new(GameObject.Create(Blueprint, Context: $"Plaidman.SaltShuffleRevival.{nameof(FactionEntity)}"), true);
			}

			return this;
		}

		public static FactionEntity GetCreature(string blueprint) {
			using var blueprintFE = new FactionEntity(blueprint);
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
			IsLovely = false;

			a = null;
			DetailColor = null;
			FgColor = null;
			FromBlueprint = false;

			Desc = null;
		}
	}
}
