using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
using Object=UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void Diagnose()
        {
            var scene=EditorSceneManager.OpenPreviewScene(Original);
            var sb=new StringBuilder();
            try
            {
                var katana=scene.GetRootGameObjects().First(g=>g.name=="Frank_Damage@Damage_Critical_Katana");
                var bones=Bones(katana.transform);
                var original=katana.GetComponent<Animator>().runtimeAnimatorController.animationClips.First();
                var raw=AssetDatabase.LoadAllAssetsAtPath(Source+"/Meshes/Frank_Damage@Damage_Critical_Katana.FBX").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview"));
                foreach(var clip in new[]{original,raw})
                {
                    sb.AppendLine(clip.name+" "+AssetDatabase.GetAssetPath(clip));
                    foreach(float time in new[]{0f,1f,2f,3f,4f,5.9f})
                    {
                        clip.SampleAnimation(katana,time);
                        sb.AppendLine(time+" root="+bones["root"].position+" rootrot="+bones["root"].eulerAngles+" pelvis="+bones["pelvis"].position+" local="+bones["pelvis"].localPosition);
                    }
                    foreach(var binding in AnimationUtility.GetCurveBindings(clip).Where(b=>b.path=="root/pelvis" && b.propertyName.Contains("Position")))
                    {
                        var curve=AnimationUtility.GetEditorCurve(clip,binding);
                        sb.AppendLine(binding.path+" "+binding.propertyName+" t0="+curve.Evaluate(0)+" t2="+curve.Evaluate(2));
                    }
                }
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
            File.WriteAllText("Temp/FrankRetarget/diagnose.txt",sb.ToString());
        }
        [Serializable] public sealed class ClipResult
        {
            public string character,role,weapon,clip,reference;
            public int frames,weapons;
            public float duration,pelvisError,contactError,weaponError,weaponRotationError,minHeight,maxHeight,maxStretch,endHoldError;
            public bool passed;
        }
        [Serializable] public sealed class Results {public ClipResult[] clips;public int controllerFrames,controllers;public string notes;}

        [MenuItem("Tools/Frank Retarget/Legacy/3. Validate every clip and controller")]
        public static void Validate()
        {
            var scene=EditorSceneManager.OpenPreviewScene(Output+"/Frank_Damages_Mankey_Pepe.unity");
            var original=EditorSceneManager.OpenPreviewScene(Original);
            var results=new System.Collections.Generic.List<ClipResult>();
            int controllerFrames=0,controllerCount=0;
            try
            {
                var actors=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<FrankPoseRetarget>(true)).ToArray();
                foreach(var group in actors.Select(a=>a.transform.parent.gameObject).Distinct())group.SetActive(true);
                foreach(var actor in actors)
                {
                    File.WriteAllText("Temp/FrankRetarget/progress",actor.name);
                    var source=original.GetRootGameObjects().First(g=>g.name.Contains("_"+actor.weapon) && g.name.EndsWith("_Hit")!=actor.isAttacker);
                    var sourceBones=Bones(source.transform);
                    foreach(var clip in actor.sourceClips)
                    {
                        AnimationClip reference;
                        if(actor.isAttacker) reference=source.GetComponent<Animator>().runtimeAnimatorController.animationClips.First(c=>c.name==clip.name);
                        else reference=AssetDatabase.LoadAllAssetsAtPath(Output+"/SourceMotion/Frank_Damage@"+clip.name+".FBX").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview"));
                        var result=new ClipResult{character=actor.characterName,role=actor.isAttacker?"Hit":"Being Hit",weapon=actor.weapon,clip=clip.name,reference=AssetDatabase.GetAssetPath(reference),duration=clip.length,frames=Mathf.CeilToInt(clip.length*60)+1,weapons=actor.weaponRenderers.Length,minHeight=float.MaxValue,maxHeight=float.MinValue,maxStretch=1};
                        if(Mathf.Abs(reference.length-clip.length)>0.001f)throw new Exception("Duration mismatch "+clip.name);
                        foreach(var renderer in actor.weaponRenderers)
                            if(!renderer.enabled)throw new Exception("Hidden weapon: "+renderer.name);
                        for(int frame=0;frame<result.frames;frame++)
                        {
                            float t=Mathf.Min(frame/60f,clip.length);
                            reference.SampleAnimation(source,t);clip.SampleAnimation(actor.gameObject,t);actor.ApplyPose();
                            Vector3 expected=sourceBones["pelvis"].position;
                            result.pelvisError=Mathf.Max(result.pelvisError,Vector3.Distance(actor.targetHips.position,expected));
                            result.minHeight=Mathf.Min(result.minHeight,expected.y);result.maxHeight=Mathf.Max(result.maxHeight,expected.y);
                            foreach(var limb in actor.limbs)
                            {
                                result.contactError=Mathf.Max(result.contactError,limb.alignGrip?Vector3.Distance(limb.TargetGrip,limb.SourceGrip):Vector3.Distance(limb.end.position,sourceBones[limb.sourceEnd.name].position));
                                result.maxStretch=Mathf.Max(result.maxStretch,limb.middle.localPosition.magnitude/limb.middleRest.magnitude);
                            }
                            foreach(var renderer in actor.weaponRenderers.OfType<SkinnedMeshRenderer>())foreach(var bone in renderer.bones)
                            {
                                var baseline=sourceBones[bone.name];
                                result.weaponError=Mathf.Max(result.weaponError,Vector3.Distance(bone.position,baseline.position));
                                result.weaponRotationError=Mathf.Max(result.weaponRotationError,Quaternion.Angle(bone.rotation,baseline.rotation));
                            }
                            foreach(var tform in actor.character.GetComponentsInChildren<Transform>(true))
                                if(float.IsNaN(tform.position.sqrMagnitude)||float.IsInfinity(tform.position.sqrMagnitude))throw new Exception("Non-finite pose: "+actor.name+" "+clip.name+" "+frame);
                        }
                        var end=actor.targetHips.position;
                        clip.SampleAnimation(actor.gameObject,clip.length+0.25f);actor.ApplyPose();
                        result.endHoldError=Vector3.Distance(end,actor.targetHips.position);
                        result.passed=result.pelvisError<0.001f && result.contactError<0.005f && result.weaponError<0.001f && result.weaponRotationError<0.1f && result.endHoldError<0.001f && result.maxStretch<=1.16f;
                        results.Add(result);
                    }
                    // Run the actual copied state machine for two full cycles, including exit
                    // blends. Force each state as well, to cover the non-default Warrior take.
                    var graph=PlayableGraph.Create("Frank controller verification");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    try
                    {
                        var controller=AnimatorControllerPlayable.Create(graph,actor.driver.runtimeAnimatorController);
                        var output=AnimationPlayableOutput.Create(graph,"driver",actor.driver);output.SetSourcePlayable(controller);graph.Play();
                        var ac=(AnimatorController)actor.driver.runtimeAnimatorController;
                        foreach(var state in ac.layers[0].stateMachine.states)
                        {
                            controller.Play(state.state.name,0,0);graph.Evaluate(0);
                            int count=Mathf.CeilToInt(((AnimationClip)state.state.motion).length*120)+2;
                            for(int frame=0;frame<count;frame++)
                            {
                                graph.Evaluate(1f/60);actor.ApplyPose();controllerFrames++;
                                if(Vector3.Distance(actor.targetHips.position,actor.sourceHips.position)>0.001f)throw new Exception("Controller pelvis drift: "+actor.name);
                                if(actor.limbs.Any(l=>l.error>0.005f))throw new Exception("Controller contact drift: "+actor.name);
                            }
                        }
                        controllerCount++;
                    }
                    finally{graph.Destroy();}
                }
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);EditorSceneManager.ClosePreviewScene(original);}
            var report=new Results{clips=results.ToArray(),controllers=controllerCount,controllerFrames=controllerFrames,
                notes="Attacks compared with original scene clips; reactions compared with the original FBX motion imported as uncompressed Generic curves because the supplied scene assigns Humanoid reaction clips to Generic avatars. The disconnected original Warrior_Hit2 state points to the first reaction; its authored alternate FBX is available in the derived controller. Attacker contact error measures anatomical grip centres; other limbs measure wrist/ankle endpoints. No source particles, trails, AudioSources or animation events exist."};
            File.WriteAllText(Output+"/Reports/validation.json",JsonUtility.ToJson(report,true));
            var csv=new StringBuilder("Character,Role,Weapon,Clip,Frames,Duration,MinHipY,MaxHipY,PelvisError,ContactError,WeaponError,WeaponAngleError,MaxLimbStretch,EndHoldError,Passed\n");
            foreach(var r in results)csv.AppendLine(string.Join(",",r.character,r.role,r.weapon,r.clip,r.frames,r.duration,r.minHeight,r.maxHeight,r.pelvisError,r.contactError,r.weaponError,r.weaponRotationError,r.maxStretch,r.endHoldError,r.passed));
            File.WriteAllText(Output+"/Reports/validation.csv",csv.ToString());AssetDatabase.Refresh();
            if(results.Any(r=>!r.passed))throw new Exception("Clip validation failed; see Reports/validation.json.");
        }
        public static void Preview()
        {
            var scene=EditorSceneManager.OpenPreviewScene(Output+"/Frank_Damages_Mankey_Pepe.unity");
            try
            {
                var roots=scene.GetRootGameObjects();
                var camera=roots.SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).First();
                camera.scene=scene;
                camera.transform.position=new Vector3(7.78f,3.5f,3.5f);
                camera.transform.LookAt(new Vector3(3.78f,1.2f,-2.5f));
                var actors=roots.SelectMany(g=>g.GetComponentsInChildren<FrankPoseRetarget>(true)).ToArray();
                foreach(var actor in actors.Where(a=>a.gameObject.activeInHierarchy))
                {
                    actor.sourceClips[0].SampleAnimation(actor.gameObject,2.5f);
                    actor.ApplyPose();
                }
                Capture(camera,"Temp/FrankRetarget/preview.png");
                var sb=new StringBuilder();
                foreach(var actor in actors.Where(a=>a.gameObject.activeInHierarchy))
                {
                    sb.AppendLine(actor.name+" hips="+actor.sourceHips.position+" target="+actor.targetHips.position+" scale="+actor.character.transform.localScale);
                    foreach(var limb in actor.limbs)sb.AppendLine(" "+limb.end.name+" source="+limb.sourceEnd.position+" target="+limb.end.position+" error="+limb.error);
                    foreach(var r in actor.character.GetComponentsInChildren<Renderer>())sb.AppendLine(" material "+r.name+" "+string.Join(",",r.sharedMaterials.Select(m=>m.name+" texture="+m.GetTexture("_BaseMap"))));
                }
                File.WriteAllText("Temp/FrankRetarget/preview.txt",sb.ToString());
            }
            finally {EditorSceneManager.ClosePreviewScene(scene);}
        }
        [MenuItem("Tools/Frank Retarget/Legacy/4. Render comparison frames")]
        public static void ComparisonFrames()
        {
            string directory=Output+"/Reports/Frames";Directory.CreateDirectory(directory);
            var clone=EditorSceneManager.OpenPreviewScene(Output+"/Frank_Damages_Mankey_Pepe.unity");
            var original=EditorSceneManager.OpenPreviewScene(Original);
            try
            {
                var actors=clone.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<FrankPoseRetarget>(true)).ToArray();
                var originals=original.GetRootGameObjects().Where(g=>g.name.StartsWith("Frank_Damage")).ToArray();
                foreach(var group in actors.Select(a=>a.transform.parent.gameObject).Distinct())group.SetActive(true);
                foreach(string weapon in Weapons)
                {
                    File.WriteAllText("Temp/FrankRetarget/progress","Rendering "+weapon);
                    var attack=originals.First(g=>g.name.Contains("_"+weapon)&&!g.name.EndsWith("_Hit"));
                    var react=originals.First(g=>g.name.Contains("_"+weapon)&&g.name.EndsWith("_Hit"));
                    var attackClip=attack.GetComponent<Animator>().runtimeAnimatorController.animationClips.First();
                    var reactClip=AssetDatabase.LoadAllAssetsAtPath(Output+"/SourceMotion/Frank_Damage@Damage_Critical_"+weapon+"_Hit.FBX").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview"));
                    foreach(var g in originals)g.SetActive(g==attack||g==react);
                    for(int variant=0;variant<3;variant++)
                    {
                        var scene=variant==0?original:clone;
                        var camera=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).First();
                        camera.scene=scene;
                        camera.orthographic=true;camera.orthographicSize=3.5f;
                        var focus=new Vector3(attack.transform.position.x,2.5f,-0.8f);
                        camera.transform.position=focus+new Vector3(5,2,8);camera.transform.LookAt(focus);
                        foreach(var actor in actors)actor.gameObject.SetActive(actor.weapon==weapon && (actor.isAttacker==(actor.characterName==(variant==1?"Mankey":"Pepe"))));
                        for(int phase=0;phase<5;phase++)
                        {
                            float time=attackClip.length*phase/4;
                            if(variant==0){attackClip.SampleAnimation(attack,time);reactClip.SampleAnimation(react,time);}
                            else foreach(var actor in actors.Where(a=>a.gameObject.activeInHierarchy))
                            {actor.sourceClips.First(c=>!c.name.EndsWith("Hit2")).SampleAnimation(actor.gameObject,time);actor.ApplyPose();}
                            Capture(camera,directory+"/"+weapon+"_"+variant+"_"+phase+".png",640,480);
                        }
                    }
                }
            }
            finally{EditorSceneManager.ClosePreviewScene(clone);EditorSceneManager.ClosePreviewScene(original);}
            AssetDatabase.Refresh();
        }
        static void Capture(Camera camera,string path,int width=1280,int height=720)
        {
            var rt=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32);
            var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;
            var image=new Texture2D(width,height,TextureFormat.RGB24,false);
            try
            {
                camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
                image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
                File.WriteAllBytes(path,image.EncodeToPNG());
            }
            finally {camera.targetTexture=oldTarget;RenderTexture.active=oldActive;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(image);}
        }
    }
}
