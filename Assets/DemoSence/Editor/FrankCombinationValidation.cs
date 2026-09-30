using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        [MenuItem("Tools/Frank Retarget/Validate all weapon combinations")]
        public static void ValidateTester()
        {
            Directory.CreateDirectory("Temp/FrankRetarget");
            var scene=EditorSceneManager.OpenScene(Output+"/Frank_Damages_Mankey_Pepe.unity",OpenSceneMode.Single);
            var tester=scene.GetRootGameObjects().Select(g=>g.GetComponent<FrankCombinationTester>()).First(t=>t);
            var mankeyModel=tester.mankey.character;var pepeModel=tester.pepe.character;
            int combinations=0,samples=0;float pelvisError=0,gripError=0,weaponGripError=0;
            var csv=new StringBuilder("attacker,motion,attackerWeapon,receiverWeapon,samples,maxPelvisError,maxGripError,maxWeaponGripError\n");
            try
            {
                for(int role=0;role<2;role++)for(int motion=0;motion<8;motion++)for(int weapon=0;weapon<8;weapon++)for(int receiverWeapon=0;receiverWeapon<8;receiverWeapon++)
                {
                    tester.gunSword=false;tester.unarmed=false;tester.pepeAttacks=role==1;tester.motion=motion;tester.attackerWeapon=weapon;tester.receiverWeapon=receiverWeapon;tester.Configure();
                    if(tester.mankey.character!=mankeyModel||tester.pepe.character!=pepeModel)throw new Exception("Visible character was replaced.");
                    if(scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<FrankTestActor>(true)).Count()!=2)throw new Exception("Expected exactly two characters.");
                    if(tester.Attacker.clip.name.Contains("_Hit")||!tester.Receiver.clip.name.Contains("_Hit"))throw new Exception("Roles reversed.");
                    if(motion==7&&!tester.Receiver.clip.name.EndsWith("Hit2"))throw new Exception("Alternate take missing.");
                    foreach(var actor in new[]{tester.Attacker,tester.Receiver})
                    {
                        if(actor.activeDriver.GetComponentsInChildren<Animator>(true).Count(a=>a.isHuman)!=0)throw new Exception("Extra character model in source driver.");
                        if(actor.Pose.originalBody.Length!=0)throw new Exception("Original character mesh retained.");
                        if(actor.equipped)
                        {
                            var anchors=new System.Collections.Generic.HashSet<Transform>(actor.equipped.bodyBones);
                            foreach(var renderer in actor.equipped.meshes.OfType<SkinnedMeshRenderer>().Where(r=>r.enabled))foreach(var bone in renderer.bones)
                            {
                                var parent=bone;
                                while(parent&&!anchors.Contains(parent))parent=parent.parent;
                                if(!parent)throw new Exception("Weapon bone does not follow a body anchor: "+bone.name);
                            }
                        }
                    }
                    float p=0,g=0,w=0;
                    // Include reverse scrubbing and repeated end holds, not only forward playback.
                    foreach(float phase in new[]{0f,.1f,.25f,.5f,.75f,.9f,1f,.3f,0f,1f})
                    {
                        tester.Seek(tester.Duration*phase);samples++;
                        foreach(var actor in new[]{tester.Attacker,tester.Receiver})
                        {
                            var pose=actor.Pose;p=Mathf.Max(p,Vector3.Distance(pose.sourceHips.position,pose.targetHips.position));
                            foreach(var l in pose.limbs.Where(l=>l.alignGrip))g=Mathf.Max(g,Vector3.Distance(l.SourceGrip,l.TargetGrip));
                            if(actor.equipped)
                            {
                                var bones=actor.equipped.bodyBones.ToDictionary(t=>t.name);
                                foreach(var l in pose.limbs.Where(l=>l.alignGrip))
                                {
                                    Vector3 grip=(bones[l.sourceKnuckle.name].position+bones[l.sourceFingerJoint.name].position)*.5f;
                                    w=Mathf.Max(w,Vector3.Distance(grip,l.TargetGrip));
                                }
                            }
                            foreach(var t in actor.character.GetComponentsInChildren<Transform>())
                                if(!float.IsFinite(t.position.sqrMagnitude)||!float.IsFinite(t.rotation.x))throw new Exception("Non-finite pose.");
                        }
                    }
                    pelvisError=Mathf.Max(pelvisError,p);gripError=Mathf.Max(gripError,g);weaponGripError=Mathf.Max(weaponGripError,w);
                    csv.AppendLine($"{tester.Attacker.characterName},{motion},{weapon},{receiverWeapon},10,{p:R},{g:R},{w:R}");combinations++;
                    if(weapon==7&&receiverWeapon==7)File.WriteAllText("Temp/FrankRetarget/tester-progress",$"{combinations}/1024 combinations");
                }
                bool passed=pelvisError<.001f&&gripError<.025f&&weaponGripError<.025f;
                string report=$"Unity: {Application.unityVersion}\nPassed: {passed}\nCombinations: {combinations}\nPose samples: {samples}\nPersistent visible characters: 2\nMaximum pelvis error: {pelvisError:R}\nMaximum anatomical grip error: {gripError:R}\nMaximum equipped weapon grip error: {weaponGripError:R}\nCoverage: 8 animation pairs × 8 attacker equipment choices × 8 receiver equipment choices × 2 role assignments.\nIncludes Warrior alternate, unarmed, forward and reverse seeking, endpoints, stable character identity, finite poses, no original body renderers.\n";
                File.WriteAllText(Output+"/Reports/combinations.txt",report);File.WriteAllText(Output+"/Reports/combinations.csv",csv.ToString());
                if(!passed)throw new Exception(report);
            }
            finally
            {
                // Restore the saved default without leaving an empty or dirty scene.
                tester.mankey.Clear();tester.pepe.Clear();
                EditorSceneManager.OpenScene(Output+"/Frank_Damages_Mankey_Pepe.unity",OpenSceneMode.Single);
            }
        }
        public static void TesterFrames()
        {
            Directory.CreateDirectory("Temp/FrankRetarget");
            string folder=Output+"/Reports/TesterFrames";Directory.CreateDirectory(folder);
            var scene=EditorSceneManager.OpenScene(Output+"/Frank_Damages_Mankey_Pepe.unity",OpenSceneMode.Single);
            var tester=scene.GetRootGameObjects().Select(g=>g.GetComponent<FrankCombinationTester>()).First(t=>t);
            var report=new StringBuilder();
            for(int weapon=1;weapon<8;weapon++)
            {
                tester.pepeAttacks=true;tester.motion=3;tester.attackerWeapon=weapon;tester.receiverWeapon=weapon;tester.Configure();
                foreach(float phase in new[]{0f,.25f,.5f,.75f,1f})
                {
                    tester.Seek(tester.Duration*phase);
                    tester.demoCamera.rect=new Rect(0,0,1,1);
                    Capture(tester.demoCamera,folder+"/"+weapon+"_"+phase.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+".png",960,720);
                    foreach(var actor in new[]{tester.Attacker,tester.Receiver})foreach(var l in actor.Pose.limbs.Where(l=>l.alignGrip))
                        report.AppendLine($"{weapon} {phase} {actor.characterName} {l.end.name}: error={Vector3.Distance(l.TargetGrip,l.SourceGrip)} solve={l.error} wristToGripSource={Vector3.Distance(l.sourceEnd.position,l.SourceGrip)} wristToGripTarget={Vector3.Distance(l.end.position,l.TargetGrip)} reach={Vector3.Distance(l.upper.position,l.Goal)} rest={l.middleRest.magnitude+l.endRest.magnitude}");
                }
            }
            File.WriteAllText(folder+"/diagnostic.txt",report.ToString());
            tester.mankey.Clear();tester.pepe.Clear();
            EditorSceneManager.OpenScene(Output+"/Frank_Damages_Mankey_Pepe.unity",OpenSceneMode.Single);
        }
        public static void InspectWeaponAttachments()
        {
            var report=new StringBuilder();
            foreach(var weapon in Weapons)
            {
                var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Output+"/Tester/Drivers/Pepe_"+weapon+"_Attack.prefab"));
                var p=root.GetComponent<FrankPoseRetarget>();
                foreach(float t in weapon=="Katana"?new[]{0f,.5f,1f,1.5f,2f,3f,4f,5f}:new[]{0f})
                {
                    p.sourceClips[0].SampleAnimation(root,t);report.AppendLine(weapon+" t="+t);
                    foreach(var r in p.weaponRenderers.OfType<SkinnedMeshRenderer>())
                    {
                        report.AppendLine(" renderer "+r.name+" scale="+r.transform.lossyScale+" enabled="+r.enabled);
                        foreach(var b in r.bones.Take(2))report.AppendLine(" "+PathOf(b,root.transform)+" pos="+b.position+" scale="+b.lossyScale+" gripdistance="+Vector3.Distance(b.position,p.limbs.First(l=>l.sourceEnd.name=="hand_r").SourceGrip));
                    }
                }
                Object.DestroyImmediate(root);
            }
            File.WriteAllText("Temp/FrankRetarget/weapon-attachments.txt",report.ToString());
        }
        public static void BuildAndValidateTester(){BuildTester();ValidateTester();}
    }
}
