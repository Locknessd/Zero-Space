using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;
namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        const string InsaneSource="Assets/InsaneGun_Sword_Set";
        const string InsaneOutput=Output+"/InsaneCombos";
        public static void SaveInsaneDefault()
        {
            var scene=EditorSceneManager.OpenScene(Output+"/Frank_Damages_Mankey_Pepe.unity",OpenSceneMode.Single);
            foreach(var leftover in scene.GetRootGameObjects().Where(g=>g.name=="combo_01(Clone)"))Object.DestroyImmediate(leftover);
            var t=Object.FindAnyObjectByType<FrankCombinationTester>();t.gunSword=true;t.unarmed=false;t.comboMotion=0;t.pepeAttacks=false;t.loop=true;t.speed=1;t.paused=false;t.Configure();t.demoCamera.rect=new Rect(0,0,1,1);EditorSceneManager.SaveScene(scene);
        }
        public static void StopInsaneCheck(){SessionState.SetBool("FrankInsanePlayCheck",false);EditorApplication.isPlaying=false;}
        sealed class SkinFloor
        {
            public Vector3[] vertices;public BoneWeight[] weights;public Transform[] bones;public Matrix4x4[] bindposes;
            public SkinFloor(SkinnedMeshRenderer skin){vertices=skin.sharedMesh.vertices;weights=skin.sharedMesh.boneWeights;bones=skin.bones;bindposes=skin.sharedMesh.bindposes;}
            public float Minimum()
            {
                if(weights.Length!=vertices.Length)return float.PositiveInfinity;
                var matrices=bones.Select((b,i)=>b.localToWorldMatrix*bindposes[i]).ToArray();float min=float.PositiveInfinity;
                for(int i=0;i<vertices.Length;i++)
                {
                    var w=weights[i];var v=vertices[i];float y=0;
                    if(w.weight0>0)y+=matrices[w.boneIndex0].MultiplyPoint3x4(v).y*w.weight0;
                    if(w.weight1>0)y+=matrices[w.boneIndex1].MultiplyPoint3x4(v).y*w.weight1;
                    if(w.weight2>0)y+=matrices[w.boneIndex2].MultiplyPoint3x4(v).y*w.weight2;
                    if(w.weight3>0)y+=matrices[w.boneIndex3].MultiplyPoint3x4(v).y*w.weight3;
                    min=Mathf.Min(min,y);
                }
                return min;
            }
        }
        static float[] GroundSamples(FrankTestActor actor,FrankComboLibrary.Pair pair)
        {
            actor.transform.SetPositionAndRotation(pair.receiverOffset,Quaternion.Euler(0,180,0));actor.ConfigureCombo(pair.reaction,false);
            var skins=actor.character.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.enabled).Select(r=>new SkinFloor(r)).ToArray();
            int count=Mathf.CeilToInt(pair.reaction.length*60)+1;var lift=new float[count];
            for(int f=0;f<count;f++)
            {
                actor.Evaluate(pair.reaction.length*f/(count-1));float min=skins.Min(s=>s.Minimum());lift[f]=Mathf.Max(0,.008f-min);
            }
            return lift;
        }
        static AnimationClip InsaneClip(string id)=>AssetDatabase.LoadAllAssetsAtPath(InsaneSource+"/Animation/Generic/"+id+".fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview"));
        static AnimationClip CopyInsaneClip(string id)
        {
            string path=InsaneOutput+"/Clips/"+id+".anim";
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(!clip){clip=Object.Instantiate(InsaneClip(id));clip.name=id;AssetDatabase.CreateAsset(clip,path);}
            clip.legacy=false;
            var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=false;settings.loopBlend=false;AnimationUtility.SetAnimationClipSettings(clip,settings);EditorUtility.SetDirty(clip);AssetDatabase.SaveAssetIfDirty(clip);return clip;
        }
        sealed class BoneSample
        {
            public Vector3[] positions,scales;public Quaternion[] rotations;
            public static BoneSample Read(Transform[] bones)=>new BoneSample{positions=bones.Select(b=>b.localPosition).ToArray(),scales=bones.Select(b=>b.localScale).ToArray(),rotations=bones.Select(b=>b.localRotation).ToArray()};
            public static BoneSample Blend(BoneSample a,BoneSample b,float t)=>new BoneSample{positions=a.positions.Select((v,i)=>Vector3.Lerp(v,b.positions[i],t)).ToArray(),scales=a.scales.Select((v,i)=>Vector3.Lerp(v,b.scales[i],t)).ToArray(),rotations=a.rotations.Select((v,i)=>Quaternion.Slerp(v,b.rotations[i],t)).ToArray()};
        }
        static float MatchStep(GameObject root,AnimationClip full,AnimationClip step)
        {
            var bones=root.GetComponentsInChildren<Transform>().Where(b=>b.name.Contains("UpperArm")||b.name.Contains("Forearm")||b.name.Contains("Thigh")||b.name.EndsWith("Spine2")||b.name.EndsWith("Pelvis")).ToArray();
            float[] times={step.length*.2f,step.length*.35f,step.length*.5f};
            var rotations=times.Select(t=>{step.SampleAnimation(root,t);return bones.Select(b=>b.localRotation).ToArray();}).ToArray();
            float best=float.PositiveInfinity,bestTime=0;
            for(float offset=0;offset<full.length-times[2];offset+=1/60f)
            {
                float error=0;for(int sample=0;sample<times.Length;sample++)
                {
                    full.SampleAnimation(root,offset+times[sample]);for(int b=0;b<bones.Length;b++)error+=Mathf.Pow(Quaternion.Angle(rotations[sample][b],bones[b].localRotation),2);
                }
                if(error<best){best=error;bestTime=offset;}
            }
            return bestTime;
        }
        static float StrikeTime(GameObject root,AnimationClip clip)
        {
            var bones=Bones(root.transform);var sword=bones["Bip001 R Hand"];var gun=bones["Bip001 L Hand"];
            float best=-1,time=clip.length*.3f;
            for(float t=.08f;t<clip.length*.65f;t+=1/60f)
            {
                clip.SampleAnimation(root,t-1/120f);var p=sword.position;var q=gun.position;var r=sword.rotation;
                clip.SampleAnimation(root,t+1/120f);
                float score=(sword.position-p).magnitude+(gun.position-q).magnitude*.4f+Quaternion.Angle(r,sword.rotation)*.003f;
                if(score>best){best=score;time=t;}
            }
            return time;
        }
        static AnimationClip GenerateStepReaction(GameObject root,AnimationClip source,AnimationClip attack,float match,float impact,out float ground)
        {
            var bones=root.GetComponentsInChildren<Transform>().Where(t=>t!=root.transform).ToArray();
            int hips=Array.FindIndex(bones,t=>t.name=="Bip001");
            BoneSample Sample(float t){source.SampleAnimation(root,Mathf.Clamp(t,0,source.length));return BoneSample.Read(bones);}
            var idle=Sample(0);
            float begin=Mathf.Max(0,impact-.06f), recoilEnd=impact+.35f,blendEnd=recoilEnd+.20f;
            float tailStart=Mathf.Max(0,source.length-.85f);
            var initial=Sample(match+impact);var recoil=Sample(match+impact+.35f);var tail=Sample(tailStart);
            // The Bip001 track carries global travel; preserve vertical action but start
            // every standalone hit at its own receiver origin, not midway across the arena.
            Vector3 recoilOrigin=initial.positions[hips];recoilOrigin.y=0;
            initial.positions[hips]-=recoilOrigin;recoil.positions[hips]-=recoilOrigin;
            recoil.positions[hips].y=Mathf.Min(recoil.positions[hips].y,idle.positions[hips].y+.4f);
            float groundHeight=Sample(source.length).positions[hips].y;
            Vector3 tailShift=recoil.positions[hips]-tail.positions[hips];tailShift.y=0;
            ground=blendEnd+(source.length-tailStart);
            float duration=Mathf.Max(attack.length,ground)+.25f;
            int count=Mathf.CeilToInt(duration*60)+1;
            var samples=new BoneSample[count];
            for(int frame=0;frame<count;frame++)
            {
                float time=duration*frame/(count-1);BoneSample pose;
                if(time<=begin)pose=BoneSample.Blend(idle,idle,0);
                else if(time<=recoilEnd)
                {
                    var hit=Sample(match+impact+Mathf.Max(0,time-impact));hit.positions[hips]-=recoilOrigin;
                    hit.positions[hips].y=Mathf.Min(hit.positions[hips].y,idle.positions[hips].y+.4f);
                    pose=BoneSample.Blend(idle,hit,Mathf.SmoothStep(0,1,Mathf.InverseLerp(begin,impact+.22f,time)));
                }
                else
                {
                    var fall=Sample(tailStart+Mathf.Max(0,time-blendEnd));fall.positions[hips]+=tailShift;
                    pose=BoneSample.Blend(recoil,fall,Mathf.SmoothStep(0,1,Mathf.InverseLerp(recoilEnd,blendEnd,time)));
                    pose.positions[hips].y=Mathf.Lerp(recoil.positions[hips].y,groundHeight,Mathf.SmoothStep(0,1,Mathf.InverseLerp(recoilEnd,ground,time)));
                }
                samples[frame]=pose;
            }
            // Smooth the re-timed root track around the splice; keep the impact pose
            // and bone rotations authored, without a one-frame translational snap.
            var rootPositions=samples.Select(sample=>sample.positions[hips]).ToArray();
            for(int f=0;f<count;f++)
            {
                Vector3 sum=Vector3.zero;float weightSum=0;
                for(int k=-5;k<=5;k++){float weight=Mathf.Exp(-k*k/10f);sum+=rootPositions[Mathf.Clamp(f+k,0,count-1)]*weight;weightSum+=weight;}
                samples[f].positions[hips]=sum/weightSum;
            }
            string path=InsaneOutput+"/Reactions/"+attack.name+"_hit_generated.anim";
            var result=new AnimationClip{name=attack.name+"_hit_generated",frameRate=60};
            for(int b=0;b<bones.Length;b++)
            {
                string bonePath=PathOf(bones[b],root.transform);
                for(int property=0;property<3;property++)for(int axis=0;axis<(property==1?4:3);axis++)
                {
                    var keys=new Keyframe[count];
                    for(int f=0;f<count;f++)
                    {
                        var q=samples[f].rotations[b];if(f>0&&Quaternion.Dot(samples[f-1].rotations[b],q)<0){q=new Quaternion(-q.x,-q.y,-q.z,-q.w);samples[f].rotations[b]=q;}
                        float v=property==0?samples[f].positions[b][axis]:property==1?q[axis]:samples[f].scales[b][axis];
                        keys[f]=new Keyframe(duration*f/(count-1),v);
                    }
                    var curve=new AnimationCurve(keys);
                    for(int k=0;k<keys.Length;k++){AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Linear);}
                    result.SetCurve(bonePath,typeof(Transform),(property==0?"m_LocalPosition.":property==1?"m_LocalRotation.":"m_LocalScale.")+"xyzw"[axis],curve);
                }
            }
            result.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(result);settings.loopTime=false;AnimationUtility.SetAnimationClipSettings(result,settings);
            var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(existing){EditorUtility.CopySerialized(result,existing);Object.DestroyImmediate(result);result=existing;EditorUtility.SetDirty(result);AssetDatabase.SaveAssetIfDirty(result);}else AssetDatabase.CreateAsset(result,path);
            return result;
        }
        [MenuItem("Tools/Frank Retarget/Add gun and sword combos")]
        public static void BuildInsaneCombos()
        {
            Folder(InsaneOutput+"/Clips");Folder(InsaneOutput+"/Reactions");Folder(InsaneOutput+"/Drivers");Folder(Output+"/Reports/InsaneCombos");
            var scene=EditorSceneManager.OpenScene(Output+"/Frank_Damages_Mankey_Pepe.unity",OpenSceneMode.Single);
            foreach(var leftover in scene.GetRootGameObjects().Where(g=>g.name=="combo_01(Clone)"))Object.DestroyImmediate(leftover);
            var tester=Object.FindAnyObjectByType<FrankCombinationTester>();tester.mankey.Clear();tester.pepe.Clear();
            var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(InsaneSource+"/Animation/Generic/combo_01.fbx"));
            var pairs=new List<FrankComboLibrary.Pair>();var report=new StringBuilder("clip,sourceMatch,impact,ground,duration\n");
            try
            {
                for(int group=1;group<=3;group++)
                {
                    string name="combo_"+group.ToString("00");var full=CopyInsaneClip(name);var hit=CopyInsaneClip(name+"_hit");
                    pairs.Add(new FrankComboLibrary.Pair{label="Full combo",sourceName=name,combo=group,step=0,attack=full,reaction=hit,receiverOffset=new Vector3(0,0,group==1?1.9f:group==2?3.89f:3.87f),groundTime=hit.length});
                    int steps=group==1?3:group==2?6:5;
                    for(int step=1;step<=steps;step++)
                    {
                        var attack=CopyInsaneClip(name+"_"+step);float match=MatchStep(root,full,attack),impact=StrikeTime(root,attack);
                        var reaction=GenerateStepReaction(root,hit,attack,match,impact,out float ground);
                        attack.SampleAnimation(root,impact);var pelvis=Bones(root.transform)["Bip001 Pelvis"].position;
                        pairs.Add(new FrankComboLibrary.Pair{label="Step "+step,sourceName=attack.name,combo=group,step=step,attack=attack,reaction=reaction,sourceMatchTime=match,impactTime=impact,groundTime=ground,receiverOffset=new Vector3(pelvis.x,0,Mathf.Max(.95f,pelvis.z+1.1f))});
                        report.AppendLine($"{attack.name},{match:R},{impact:R},{ground:R},{reaction.length:R}");
                    }
                }
                var clips=pairs.SelectMany(p=>new[]{p.attack,p.reaction}).Distinct().ToArray();
                string model=InsaneSource+"/Animation/Generic/combo_01.fbx",avatar=InsaneSource+"/Animation/Humanoid/00_T-pose_InsaneGun_Sword.fbx";
                tester.mankey.comboDriver=MakeCalibratedDriver(tester.mankey,clips,model,avatar,InsaneOutput,"InsaneCombos",true,out float scale);
                tester.pepe.comboDriver=MakeCalibratedDriver(tester.pepe,clips,model,avatar,InsaneOutput,"InsaneCombos",true,out float otherScale);
                if(Mathf.Abs(scale-otherScale)>.001f)throw new Exception("Pair scale differs");
                foreach(var pair in pairs)
                {
                    pair.receiverOffset*=scale;
                    pair.mankeyFloor=GroundSamples(tester.mankey,pair);pair.pepeFloor=GroundSamples(tester.pepe,pair);
                }
                string path=InsaneOutput+"/ComboLibrary.asset";var library=AssetDatabase.LoadAssetAtPath<FrankComboLibrary>(path);
                if(!library){library=ScriptableObject.CreateInstance<FrankComboLibrary>();AssetDatabase.CreateAsset(library,path);}
                library.pairs=pairs.ToArray();EditorUtility.SetDirty(library);AssetDatabase.SaveAssetIfDirty(library);
                tester.comboLibrary=library;tester.gunSword=true;tester.unarmed=false;tester.comboMotion=0;tester.paused=false;tester.Configure();tester.demoCamera.rect=new Rect(0,0,1,1);
                Object.DestroyImmediate(root);root=null;
                EditorSceneManager.SaveScene(scene);File.WriteAllText(Output+"/Reports/InsaneCombos/generation.csv",report.ToString());
                File.WriteAllText("Temp/FrankRetarget/insane-build.txt","17 choices: 3 full combos + 14 steps, 14 generated knockdowns. Scale="+scale);
            }
            finally{Object.DestroyImmediate(root);}
        }
        public static void InspectInsane()
        {
            var scene=EditorSceneManager.OpenPreviewScene(InsaneSource+"/InsaneGun_Sword_Set.unity");var log=new StringBuilder();
            try
            {
                foreach(var a in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Animator>(true)))
                {
                    if(!a.name.Contains("combo"))continue;
                    log.AppendLine($"ACTOR {a.name} position={a.transform.position} rotation={a.transform.eulerAngles} scale={a.transform.lossyScale} avatar={AssetDatabase.GetAssetPath(a.avatar)} human={a.isHuman}");
                    if(a.runtimeAnimatorController)foreach(var c in a.runtimeAnimatorController.animationClips.Distinct())log.AppendLine($"CLIP {c.name} duration={c.length} human={c.humanMotion} path={AssetDatabase.GetAssetPath(c)}");
                    foreach(var r in a.GetComponentsInChildren<Renderer>(true))log.AppendLine("RENDER "+PathOf(r.transform,a.transform)+" materials="+string.Join(",",r.sharedMaterials.Select(m=>m?m.name:"null")));
                }
                foreach(string id in new[]{"combo_01","combo_01_hit","combo_01_1","combo_02","combo_02_hit","combo_03","combo_03_hit"})
                {
                    string path=InsaneSource+"/Animation/Generic/"+id+".fbx";
                    var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));var clip=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview"));
                    log.AppendLine("MODEL "+id);
                    if(id=="combo_01")foreach(var t in root.GetComponentsInChildren<Transform>())log.AppendLine("BONE "+PathOf(t,root.transform));
                    foreach(float fraction in new[]{0f,.2f,.4f,.6f,.8f,1f})
                    {
                        clip.SampleAnimation(root,clip.length*fraction);
                        foreach(var b in root.GetComponentsInChildren<Transform>().Where(t=>t.name.ToLower().Contains("pelvis")||t.name.ToLower().Contains("hips")))log.AppendLine($"POSE {fraction} {b.name} {b.position} rot={b.eulerAngles}");
                    }
                    Object.DestroyImmediate(root);
                }
                File.WriteAllText("Temp/FrankRetarget/insane-inventory.txt",log.ToString());
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
