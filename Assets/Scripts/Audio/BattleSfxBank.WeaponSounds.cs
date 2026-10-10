using System;
using UnityEngine;

public sealed partial class BattleSfxBank
{
    [Serializable]
    public sealed class WeaponSoundSet
    {
        public TrumpWeaponManager.WeaponType weapon;
        public string swing;
        public string heavySwing;
        public string thrustSwing;
        public string lightHit;
        public string heavyHit;
        public string stabHit;
    }

    public WeaponSoundSet[] weaponSounds = Array.Empty<WeaponSoundSet>();
    public string shieldSwingGroup;
    public string shieldHitGroup;

    public string ResolveWeaponGroup(CombatTripletData move, Cue cue, Move profile = null)
    {
        if (move == null || cue == null || move.weapon == TrumpWeaponManager.WeaponType.None ||
            move.skill != BattleSkill.None)
            return cue?.group;
        bool swing = cue.group != null && cue.group.EndsWith("swing", StringComparison.Ordinal);
        string source = cue.contactSource;
        if (swing && string.IsNullOrEmpty(source))
        {
            foreach (var next in profile?.cues ?? Array.Empty<Cue>())
            {
                if (next.seconds <= cue.seconds || next.seconds - cue.seconds > .65f)
                    continue;
                if (next.group != "light_hit" && next.group != "heavy_hit" && next.group != "stab_hit")
                    continue;
                source = next.contactSource;
                break;
            }
            if (string.IsNullOrEmpty(source) && cue.group != "light_swing")
                source = "Weapon";
        }
        string selected = null;
        if (source == "Shield")
            selected = swing ? shieldSwingGroup : IsWeaponHit(cue.group) ? shieldHitGroup : null;
        else if (source == "Weapon")
        {
            var sound = Array.Find(weaponSounds ?? Array.Empty<WeaponSoundSet>(),
                entry => entry != null && entry.weapon == move.weapon);
            if (sound != null)
            {
                switch (cue.group)
                {
                    case "light_swing":
                    case "blade_swing":
                        selected = sound.swing;
                        break;
                    case "heavy_swing":
                        selected = sound.heavySwing;
                        break;
                    case "thrust_swing":
                        selected = sound.thrustSwing;
                        break;
                    case "light_hit":
                        selected = sound.lightHit;
                        break;
                    case "heavy_hit":
                        selected = sound.heavyHit;
                        break;
                    case "stab_hit":
                        selected = sound.stabHit;
                        break;
                }
            }
        }
        return !string.IsNullOrEmpty(selected) && FindGroup(selected)?.clips?.Length > 0 ? selected : cue.group;
    }

    static bool IsWeaponHit(string group) => group == "light_hit" || group == "heavy_hit" || group == "stab_hit";
}
