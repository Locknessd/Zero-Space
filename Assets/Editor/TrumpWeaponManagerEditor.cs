using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TrumpWeaponManager))]
public class TrumpWeaponManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawDefaultInspector();

        var manager = (TrumpWeaponManager)target;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Quick Weapon Switch", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Warrior Shield"))
        {
            Undo.RecordObject(manager, "Equip Warrior Shield");
            manager.EquipWeapon(TrumpWeaponManager.WeaponType.WarriorShield);
            EditorUtility.SetDirty(manager);
        }
        if (GUILayout.Button("GreatSword"))
        {
            Undo.RecordObject(manager, "Equip GreatSword");
            manager.EquipWeapon(TrumpWeaponManager.WeaponType.GreatSword);
            EditorUtility.SetDirty(manager);
        }
        if (GUILayout.Button("Spear"))
        {
            Undo.RecordObject(manager, "Equip Spear");
            manager.EquipWeapon(TrumpWeaponManager.WeaponType.Spear);
            EditorUtility.SetDirty(manager);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Katana"))
        {
            Undo.RecordObject(manager, "Equip Katana");
            manager.EquipWeapon(TrumpWeaponManager.WeaponType.Katana);
            EditorUtility.SetDirty(manager);
        }
        if (GUILayout.Button("Dual Daggers"))
        {
            Undo.RecordObject(manager, "Equip Dual Daggers");
            manager.EquipWeapon(TrumpWeaponManager.WeaponType.DualDaggers);
            EditorUtility.SetDirty(manager);
        }
        if (GUILayout.Button("Assassin"))
        {
            Undo.RecordObject(manager, "Equip Assassin");
            manager.EquipWeapon(TrumpWeaponManager.WeaponType.Assassin);
            EditorUtility.SetDirty(manager);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("2-Handed Axe"))
        {
            Undo.RecordObject(manager, "Equip 2-Handed Axe");
            manager.EquipWeapon(TrumpWeaponManager.WeaponType.TwoHandedAxe);
            EditorUtility.SetDirty(manager);
        }
        GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
        if (GUILayout.Button("Unequip All (Barehands)"))
        {
            Undo.RecordObject(manager, "Unequip All");
            manager.EquipWeapon(TrumpWeaponManager.WeaponType.None);
            EditorUtility.SetDirty(manager);
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        serializedObject.ApplyModifiedProperties();
    }
}
