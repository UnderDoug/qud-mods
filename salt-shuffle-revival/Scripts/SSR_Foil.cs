using System;
using System.Collections.Generic;
using System.Text;

namespace XRL.World.Parts
{
    public class Mod_SSR_Foil : IModification
    {
        public Mod_SSR_Foil()
            : base()
        { }

        public override void Initialize()
        {
            ParentObject.RequirePart<SSR_AnimatedMaterialFoil>();
            base.Initialize();
        }

        public override void Remove()
        {
            ParentObject.RemovePart<SSR_AnimatedMaterialFoil>();
            base.Remove();
        }

        public override bool WantEvent(int ID, int cascade)
            => base.WantEvent(ID, cascade)
            || ID == GetDisplayNameEvent.ID
            || ID == GetIntrinsicValueEvent.ID
            ;

        public override bool HandleEvent(GetDisplayNameEvent E)
        {
            if (E.Object == ParentObject)
                E.AddAdjective("{{Y|foil}}");

            return base.HandleEvent(E);
        }

        public override bool HandleEvent(GetIntrinsicValueEvent E)
        {
            if (E.Object == ParentObject)
                SSR_Card.OverValueMulti(ref E.Value, 4.0);
            return base.HandleEvent(E);
        }
    }
}
