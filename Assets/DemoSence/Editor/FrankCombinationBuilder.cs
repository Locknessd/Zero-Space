using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        static string PathOf(Transform t,Transform root)=>t?AnimationUtility.CalculateTransformPath(t,root):"";
        static FrankTestDriver MakeTestDriver(string character,string weapon,bool attack)
        {
            string path=Output+"/Sets/"+character+"/"+(attack?"Hit":"Being Hit")+"/"+weapon+".prefab";
            var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            var bridge=root.GetComponent<FrankPoseRetarget>();bridge.enabled=false;
            // Standard Frank source clips are Generic transform clips. A Humanoid avatar
            // on this preview Animator makes Unity ignore those transform bindings, so
            // keep the explicit source avatar only for HumanPoseHandler and leave the
            // playback Animator avatar-free.
            if(bridge.driver)bridge.driver.avatar=null;
            var target=bridge.character;
            // Receiver clips also support a held weapon. Calibrate fingers against the same
            // persistent target, without forcing a grasp for an unarmed reaction.
            if(!attack)
            {
                var fingers=new List<FrankPoseRetarget.Finger>();
                foreach(var side in new[]{"Left","Right"})foreach(var digit in new[]{"Thumb","Index","Middle","Ring","Little"})
                {
                    string prefix=side+" "+digit+" ";
                    if(!target.avatar.humanDescription.human.Any(h=>h.humanName==prefix+"Proximal"))continue;
                    fingers.Add(new FrankPoseRetarget.Finger{
                        sourceProximal=HumanBone(root.transform,bridge.sourceHumanAvatar,prefix+"Proximal"),
                        sourceIntermediate=HumanBone(root.transform,bridge.sourceHumanAvatar,prefix+"Intermediate"),
                        sourceDistal=HumanBone(root.transform,bridge.sourceHumanAvatar,prefix+"Distal"),
                        proximal=HumanBone(target.transform,target.avatar,prefix+"Proximal"),
                        intermediate=HumanBone(target.transform,target.avatar,prefix+"Intermediate"),
                        distal=HumanBone(target.transform,target.avatar,prefix+"Distal")});
                }
                bridge.fingers=fingers.ToArray();
            }
            var driver=root.AddComponent<FrankTestDriver>();driver.pose=bridge;driver.characterScale=target.transform.localScale;
            driver.hips=PathOf(bridge.targetHips,target.transform);
            driver.targetLimbs=bridge.limbs.Select(l=>new FrankTestDriver.TargetLimb{
                upper=PathOf(l.upper,target.transform),middle=PathOf(l.middle,target.transform),end=PathOf(l.end,target.transform),
                knuckle=PathOf(l.targetKnuckle,target.transform),joint=PathOf(l.targetFingerJoint,target.transform)}).ToArray();
            driver.targetFingers=bridge.fingers.Select(f=>new FrankTestDriver.TargetFinger{
                proximal=PathOf(f.proximal,target.transform),intermediate=PathOf(f.intermediate,target.transform),distal=PathOf(f.distal,target.transform)}).ToArray();
            // Save one reusable visible model, independent of all source skeletons.
            string modelPath=Output+"/Tester/"+character+".prefab";
            if(weapon=="2Handed"&&attack)PrefabUtility.SaveAsPrefabAsset(target.gameObject,modelPath);
            Object.DestroyImmediate(target.gameObject);
            foreach(var r in bridge.originalBody)if(r)Object.DestroyImmediate(r);
            bridge.originalBody=Array.Empty<Renderer>();
            foreach(var a in root.GetComponentsInChildren<Animator>())if(a!=bridge.driver)Object.DestroyImmediate(a);
            root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            root.name=character+"_"+weapon+"_"+(attack?"Attack":"Reaction");
            string driverPath=Output+"/Tester/Drivers/"+root.name+".prefab";
            var saved=PrefabUtility.SaveAsPrefabAsset(root,driverPath).GetComponent<FrankTestDriver>();
            Object.DestroyImmediate(root);return saved;
        }
        static FrankWeaponRig MakeWeapon(FrankTestDriver source,int index)
        {
            var root=Object.Instantiate(source.gameObject);
            var profile=root.GetComponent<FrankTestDriver>();var bridge=profile.pose;
            bridge.sourceClips[0].SampleAnimation(root,index==4?1.0f:0);
            var rig=root.AddComponent<FrankWeaponRig>();
            var map=Bones(root.transform);
            var names=new HashSet<string>(bridge.sourceHumanAvatar.humanDescription.human.Select(h=>h.boneName));
            // Hierarchy order is important: apply parents before their children.
            rig.bodyBones=root.GetComponentsInChildren<Transform>(true).Where(t=>names.Contains(t.name)).ToArray();
            rig.fingerBones=bridge.sourceHumanAvatar.humanDescription.human.Where(h=>h.humanName.Contains(" ")).Select(h=>map[h.boneName]).ToArray();
            rig.gripRotations=rig.fingerBones.Select(t=>t.localRotation).ToArray();
            rig.meshes=bridge.weaponRenderers;
            foreach(var renderer in rig.meshes)renderer.enabled=true;
            if(index==4)
            {
                // Katana's original blade is driven by a separate IK prop track. For a
                // foreign motion, preserve its drawn grip and parent that blade to the hand.
                // The dummy is only the original sheathing animation's duplicate blade.
                map["Weapon_Sword"].SetParent(map["hand_r"],true);
                rig.meshes.First(r=>r.name=="Sword_Dummy").enabled=false;
            }
            foreach(var animator in root.GetComponentsInChildren<Animator>())Object.DestroyImmediate(animator);
            Object.DestroyImmediate(bridge);Object.DestroyImmediate(profile);
            root.name=Weapons[index];
            var saved=PrefabUtility.SaveAsPrefabAsset(root,Output+"/Tester/Weapons/"+Weapons[index]+".prefab").GetComponent<FrankWeaponRig>();
            Object.DestroyImmediate(root);return saved;
        }
        [MenuItem("Tools/Frank Retarget/Build two-character combination tester")]
        public static void BuildTester()
        {
            Directory.CreateDirectory("Temp/FrankRetarget");
            Folder(Output+"/Tester/Drivers");Folder(Output+"/Tester/Weapons");
            var source=EditorSceneManager.OpenPreviewScene(Original);
            var drivers=new FrankTestDriver[2,2,7];
            for(int c=0;c<2;c++)for(int role=0;role<2;role++)for(int w=0;w<7;w++)
                drivers[c,role,w]=MakeTestDriver(c==0?"Mankey":"Pepe",Weapons[w],role==0);
            var weaponAssets=Enumerable.Range(0,7).Select(i=>MakeWeapon(drivers[0,0,i],i)).ToArray();
            string scenePath=Output+"/Frank_Damages_Mankey_Pepe.unity";
            var scene=EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Single);
            foreach(var old in scene.GetRootGameObjects())
                if(old.GetComponentsInChildren<FrankPoseRetarget>(true).Length>0||old.GetComponent<FrankDemoControls>()||old.GetComponent<FrankCombinationTester>()||old.GetComponent<FrankTestActor>())Object.DestroyImmediate(old);
            var tester=new GameObject("Animation and weapon tester").AddComponent<FrankCombinationTester>();
            tester.weapons=Weapons.Select(w=>AssetDatabase.LoadAssetAtPath<GameObject>(Output+"/Tester/Weapons/"+w+".prefab").GetComponent<FrankWeaponRig>()).ToArray();
            tester.receiverOffsets=new Vector3[7];tester.receiverRotations=new Quaternion[7];
            var originals=source.GetRootGameObjects();
            for(int w=0;w<7;w++)
            {
                var attack=originals.First(g=>g.name=="Frank_Damage@Damage_Critical_"+Weapons[w]);
                var receiver=originals.First(g=>g.name=="Frank_Damage_"+Weapons[w]+"_Hit");
                tester.receiverOffsets[w]=receiver.transform.position-attack.transform.position;
                tester.receiverRotations[w]=receiver.transform.rotation;
            }
            for(int c=0;c<2;c++)
            {
                string name=c==0?"Mankey":"Pepe";
                var actor=new GameObject(name+" character").AddComponent<FrankTestActor>();actor.characterName=name;
                actor.character=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Output+"/Tester/"+name+".prefab"),actor.transform,false).GetComponent<Animator>();
                actor.character.gameObject.name=name;
                actor.attackDrivers=Weapons.Select(w=>AssetDatabase.LoadAssetAtPath<GameObject>(Output+"/Tester/Drivers/"+name+"_"+w+"_Attack.prefab").GetComponent<FrankTestDriver>()).ToArray();
                actor.reactionDrivers=Weapons.Select(w=>AssetDatabase.LoadAssetAtPath<GameObject>(Output+"/Tester/Drivers/"+name+"_"+w+"_Reaction.prefab").GetComponent<FrankTestDriver>()).ToArray();
                if(c==0)tester.mankey=actor;else tester.pepe=actor;
            }
            tester.demoCamera=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).First();
            tester.demoCamera.clearFlags=CameraClearFlags.SolidColor;
            tester.demoCamera.backgroundColor=new Color(.12f,.16f,.21f);
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight=new Color(.65f,.68f,.74f);
            var lights=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Light>()).ToArray();
            for(int i=0;i<lights.Length;i++)
            {
                var animator=lights[i].GetComponent<Animator>();if(animator)Object.DestroyImmediate(animator);
                lights[i].enabled=true;lights[i].color=Color.white;lights[i].intensity=i==0?1.5f:.8f;
                lights[i].transform.rotation=Quaternion.Euler(i==0?50:30,i==0?-35:140,0);
                lights[i].shadows=i==0?LightShadows.Soft:LightShadows.None;
            }
            string stagePath=Output+"/Materials/TesterStage.mat";
            var stage=AssetDatabase.LoadAssetAtPath<Material>(stagePath);
            if(!stage){stage=new Material(Shader.Find("Universal Render Pipeline/Lit"));stage.SetColor("_BaseColor",new Color(.24f,.28f,.34f));stage.SetFloat("_Smoothness",.15f);AssetDatabase.CreateAsset(stage,stagePath);}
            var plane=scene.GetRootGameObjects().First(g=>g.name=="Plane").GetComponent<Renderer>();plane.sharedMaterial=stage;
            tester.Configure();
            // The saved scene contains exactly two character models and two mesh-free
            // motion skeletons. Runtime configuration reuses the same visible model instances.
            tester.demoCamera.rect=new Rect(0,0,1,1);
            EditorSceneManager.SaveScene(scene,scenePath);
            EditorSceneManager.ClosePreviewScene(source);
            if(File.Exists(Vol10Output+"/UnarmedLibrary.asset"))BuildVol10();
            if(File.Exists(InsaneOutput+"/ComboLibrary.asset"))BuildInsaneCombos();
            File.WriteAllText("Temp/FrankRetarget/tester-build.txt","Built two persistent characters, 28 mesh-free calibrated driver assets, 7 independently selectable weapon assets.");
        }
    }
}
