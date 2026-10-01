using System;
using UnityEngine;

[Serializable]
public class CombatTripletData
{
    [Header("Thông tin cơ bản")]
    public string moveName;

    [Header("Bộ 3 Animation")]
    public AnimationClip attackAnim;
    public AnimationClip hitAnim;
    public AnimationClip getUpAnim;

    [Header("Thông số khoảng cách")]
    [Min(0.01f)] public float attackRange = 2f;
    public TrumpWeaponManager.WeaponType weapon;
    public FrankRetarget.FrankBattlePair sourcePair;

    public bool IsValid => attackAnim != null && hitAnim != null &&
                           float.IsFinite(attackRange) && attackRange > 0f;
}
