using System;
using UnityEngine;

public enum BattleSkill { None, Archer, WhiteMage }

[Serializable]
public class CombatTripletData
{
    [Header("Thông tin cơ bản")]
    public string moveName;
    public CombatActionDefinition actionDefinition;

    [Header("Bộ 3 Animation")]
    public AnimationClip attackAnim;
    public AnimationClip hitAnim;
    public AnimationClip getUpAnim;

    [Header("Thông số khoảng cách")]
    [Min(0.01f)] public float attackRange = 2f;
    public TrumpWeaponManager.WeaponType weapon;
    public BattleSkill skill;
    public FrankRetarget.FrankBattlePair sourcePair;
    public FrankRetarget.FrankGrappleDefinition grapple;
    public FrankRetarget.FrankGrappleOutcome grappleOutcome;
    public bool requiresExplicitSelection;

    public bool IsValid => attackAnim != null && hitAnim != null &&
                           float.IsFinite(attackRange) && attackRange > 0f && (!grapple || grapple.Valid);
}
