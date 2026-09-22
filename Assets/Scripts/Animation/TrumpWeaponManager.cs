using System;
using UnityEngine;

/// <summary>
/// Controls weapon equipping and toggling for Trump (and other humanoid models).
/// Allows flexible switching between all 7 Frank weapon sets in Edit Mode and Runtime,
/// both manually via Inspector/API and automatically synced with playing animations.
///
/// AUTO-EQUIP RULE: the weapon is shown ONLY while a weapon-specific ATTACK state is playing.
/// Every other state (idle, hit, getup, knockdown, run, walk, die, ...) reverts to
/// <see cref="defaultIdleWeapon"/>, which defaults to None = bare hands.
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public class TrumpWeaponManager : MonoBehaviour
{
    public enum WeaponType
    {
        None = 0,
        WarriorShield = 1,
        GreatSword = 2,
        Spear = 3,
        Katana = 4,
        DualDaggers = 5,
        Assassin = 6,
        TwoHandedAxe = 7
    }

    [Header("Current Weapon")]
    [Tooltip("Select active weapon. In Editor mode, changing this dropdown updates the model immediately.")]
    [SerializeField] private WeaponType activeWeapon = WeaponType.None;

    [Header("Automatic Animation Sync")]
    [Tooltip("If true, automatically switches weapon when Animator enters a weapon-specific attack state.")]
    public bool autoEquipWithAnimation = true;

    [Tooltip("Weapon used for EVERY non-attack state (idle, hit, getup, knockdown, run, walk, die). Defaults to None so the model auto-unequips whenever it is not performing a weapon attack.")]
    public WeaponType defaultIdleWeapon = WeaponType.None;

    [Header("Weapon Set GameObjects")]
    public GameObject warriorShieldSet;
    public GameObject greatSwordSet;
    public GameObject spearSet;
    public GameObject katanaSet;
    public GameObject dualDaggersSet;
    public GameObject assassinSet;
    public GameObject twoHandedAxeSet;

    [Header("Katana Details (Optional)")]
    [Tooltip("Mesh of the sword in hand.")]
    public GameObject katanaSwordMesh;
    [Tooltip("Mesh of the sheath / scabbard on hip.")]
    public GameObject katanaCaseMesh;
    [Tooltip("Mesh of the dummy hilt inside sheath (used when katana is sheathed).")]
    public GameObject katanaDummyHandle;

    [Header("Animator Reference")]
    [SerializeField] private Animator characterAnimator;

    public WeaponType ActiveWeapon
    {
        get => activeWeapon;
        set => EquipWeapon(value);
    }

    private void Awake()
    {
        if (characterAnimator == null)
        {
            characterAnimator = GetComponent<Animator>();
            if (characterAnimator == null)
            {
                characterAnimator = GetComponentInChildren<Animator>();
            }
        }
    }

    private void Start()
    {
        ApplyWeaponVisibility(activeWeapon);
    }

    private void OnValidate()
    {
        // Immediate visual update in Edit Mode when changing the dropdown in Inspector
        ApplyWeaponVisibility(activeWeapon);
    }

    private void Update()
    {
        if (!Application.isPlaying || !autoEquipWithAnimation || characterAnimator == null)
        {
            return;
        }

        CheckAnimatorStateAndAutoEquip();
    }

    /// <summary>
    /// Equips the specified weapon and deactivates all other weapon sets.
    /// </summary>
    public void EquipWeapon(WeaponType type)
    {
        activeWeapon = type;
        ApplyWeaponVisibility(type);
    }

    /// <summary>
    /// Unequips all weapons.
    /// </summary>
    [ContextMenu("Unequip All")]
    public void UnequipAll()
    {
        EquipWeapon(WeaponType.None);
    }

    [ContextMenu("Equip Warrior Shield")]
    public void EquipWarriorShield() => EquipWeapon(WeaponType.WarriorShield);

    [ContextMenu("Equip GreatSword")]
    public void EquipGreatSword() => EquipWeapon(WeaponType.GreatSword);

    [ContextMenu("Equip Spear")]
    public void EquipSpear() => EquipWeapon(WeaponType.Spear);

    [ContextMenu("Equip Katana")]
    public void EquipKatana() => EquipWeapon(WeaponType.Katana);

    [ContextMenu("Equip Dual Daggers")]
    public void EquipDualDaggers() => EquipWeapon(WeaponType.DualDaggers);

    [ContextMenu("Equip Assassin")]
    public void EquipAssassin() => EquipWeapon(WeaponType.Assassin);

    [ContextMenu("Equip 2-Handed Axe")]
    public void EquipTwoHandedAxe() => EquipWeapon(WeaponType.TwoHandedAxe);

    private void ApplyWeaponVisibility(WeaponType type)
    {
        SetObjectActive(warriorShieldSet, type == WeaponType.WarriorShield);
        SetObjectActive(greatSwordSet, type == WeaponType.GreatSword);
        SetObjectActive(spearSet, type == WeaponType.Spear);
        SetObjectActive(katanaSet, type == WeaponType.Katana);
        SetObjectActive(dualDaggersSet, type == WeaponType.DualDaggers);
        SetObjectActive(assassinSet, type == WeaponType.Assassin);
        SetObjectActive(twoHandedAxeSet, type == WeaponType.TwoHandedAxe);

        // Katana special handling: the sheath + sword are shown together ONLY while the katana is
        // equipped. They are explicitly HIDDEN for None / any other set, so the meshes can never be
        // left visible after unequipping.
        bool katana = type == WeaponType.Katana;
        if (katanaCaseMesh != null) katanaCaseMesh.SetActive(katana);
        if (katanaSwordMesh != null) katanaSwordMesh.SetActive(katana);
        if (katanaDummyHandle != null) katanaDummyHandle.SetActive(katana);
    }

    private void SetObjectActive(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active)
        {
            go.SetActive(active);
        }
    }

    private void CheckAnimatorStateAndAutoEquip()
    {
        if (characterAnimator == null) return;

        var state = characterAnimator.GetCurrentAnimatorStateInfo(0);

        // Resolve the weapon the CURRENT state needs. WeaponType.None means "not a weapon attack state"
        // (idle, hit, getup, run, walk, die, ...) -> fall back to defaultIdleWeapon (None).
        WeaponType required = ResolveWeaponForState(state);
        if (required == WeaponType.None) required = defaultIdleWeapon;

        if (activeWeapon != required)
        {
            EquipWeapon(required);
        }
    }

    /// <summary>
    /// Maps an animator state to the weapon it needs, or <see cref="WeaponType.None"/> when the state is
    /// not a weapon attack. Expressing the rule as a single lookup means ANY unrecognised state (a hit
    /// reaction, a getup, running, dying, ...) automatically counts as "no weapon" instead of silently
    /// keeping the previous set equipped.
    /// </summary>
    private WeaponType ResolveWeaponForState(AnimatorStateInfo state)
    {
        // NOTE: the hit reactions (hit_combo_weapon*) are deliberately NOT listed here. A fighter that
        // is being hit is staggering bare-handed -- it must NOT keep holding the attacker's weapon.
        // Only the ATTACK states below equip a weapon.

        // Warrior / shield (AttackIndex 7 family).
        if (state.IsName("combo_weapon(7)") || state.IsTag("Warrior"))
            return WeaponType.WarriorShield;

        // Great sword (AttackIndex 5 family).
        if (state.IsName("combo_weapon(5)") || state.IsTag("GreatSword"))
            return WeaponType.GreatSword;

        // Spear (AttackIndex 6 family).
        if (state.IsName("combo_weapon(6)") || state.IsTag("Spear"))
            return WeaponType.Spear;

        // Katana.
        if (state.IsName("combo_weapon(9)") || state.IsTag("Katana"))
            return WeaponType.Katana;

        // Dual daggers.
        if (state.IsName("combo_weapon(8)") || state.IsTag("Dual"))
            return WeaponType.DualDaggers;

        // Assassin.
        if (state.IsName("combo_weapon(4)") || state.IsTag("Assassin"))
            return WeaponType.Assassin;

        // Two-handed axe.
        if (state.IsName("combo_weapon(2)") || state.IsTag("2Handed"))
            return WeaponType.TwoHandedAxe;

        // Everything else -> no weapon. This covers:
        //   - hit reactions: hit3, hit5, hit_combo_weapon 1/6/7, Damage_Critical_*_Hit
        //   - idle / locomotion: Idle, Enemy1_Idle, Enemy1_Walk, Enemy1_Run
        //   - knockdown / getup / death: KB_Idle_1, Combo_Getup01/02, KB_TopKO
        return WeaponType.None;
    }
}
