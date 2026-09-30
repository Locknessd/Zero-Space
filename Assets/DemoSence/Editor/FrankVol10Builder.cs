using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;
namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        const string Vol10Source="Assets/Asstes/FightingUnityChan_Asset10";
        const string Vol10Scene="Assets/Asstes/Scenes/Vol10_EF-12 AnimationPreview.unity";
        const string Vol10Output=Output+"/Vol10";
        [MenuItem("Tools/Frank Retarget/Add or rebuild Vol10 bare-hand library")]
        public static void AddVol10(){PrepareVol10();BuildVol10();}
        public static void PrepareVol10()
        {
            Folder(Vol10Output+"/Clips");Folder(Vol10Output+"/Drivers");
            string path=Vol10Output+"/Calibration.fbx";
            if(!File.Exists(path))File.Copy(Vol10Source+"/Models/unitychan.fbx",path);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(ModelImporter)AssetImporter.GetAtPath(path);
            importer.materialImportMode=ModelImporterMaterialImportMode.None;
            importer.animationType=ModelImporterAnimationType.Human;
            importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
            importer.optimizeGameObjects=false;importer.importAnimation=false;
            importer.SaveAndReimport();
            var avatar=AvatarAt(path);
            if(!avatar || !avatar.isValid || !avatar.isHuman)throw new Exception("Invalid Vol10 calibration avatar");
            File.WriteAllText("Temp/FrankRetarget/vol10-avatar.txt",string.Join("\n",avatar.humanDescription.human.Select(h=>h.humanName+"="+h.boneName)));
        }
        static FrankTestDriver MakeVol10Driver(FrankTestActor actor,AnimationClip[] clips,out float scale)
        {return MakeCalibratedDriver(actor,clips,Vol10Source+"/Models/unitychan.fbx",Vol10Output+"/Calibration.fbx",Vol10Output,"Vol10",false,out scale);}
        static FrankTestDriver MakeCalibratedDriver(FrankTestActor actor,AnimationClip[] clips,string modelPath,string avatarPath,string output,string label,bool weapons,out float scale)
        {
            var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
            var avatar=AvatarAt(avatarPath);
            ReferencePose(root.transform,avatar);
            var target=Object.Instantiate(actor.character.gameObject).GetComponent<Animator>();
            target.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            target.transform.localScale=actor.attackDrivers[0].characterScale;
            ReferencePose(target.transform,target.avatar);
            Func<string,Transform> sb=n=>HumanBone(root.transform,avatar,n);
            Func<string,Transform> tb=n=>HumanBone(target.transform,target.avatar,n);
            float sourceLeg=Vector3.Distance(sb("LeftUpperLeg").position,sb("LeftLowerLeg").position)+Vector3.Distance(sb("LeftLowerLeg").position,sb("LeftFoot").position);
            float targetLeg=Vector3.Distance(tb("LeftUpperLeg").position,tb("LeftLowerLeg").position)+Vector3.Distance(tb("LeftLowerLeg").position,tb("LeftFoot").position);
            scale=targetLeg/sourceLeg;root.transform.localScale*=scale;
            root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            foreach(var legacy in root.GetComponentsInChildren<Animation>())Object.DestroyImmediate(legacy);
            var source=root.GetComponent<Animator>();if(!source)source=root.AddComponent<Animator>();source.runtimeAnimatorController=null;source.applyRootMotion=false;source.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            var weaponMeshes=root.GetComponentsInChildren<Renderer>(true).Where(r=>weapons&&r.name.StartsWith("WP_")).ToArray();
            foreach(var r in root.GetComponentsInChildren<Renderer>(true))
                if(weaponMeshes.Contains(r))r.sharedMaterials=r.sharedMaterials.Select(m=>MaterialFor(m,label)).ToArray();else Object.DestroyImmediate(r);
            foreach(var c in root.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
            var pose=root.AddComponent<FrankPoseRetarget>();pose.enabled=false;
            pose.driver=source;pose.sourceHumanAvatar=avatar;pose.character=target;
            pose.sourceHips=sb("Hips");pose.targetHips=tb("Hips");pose.characterName=actor.characterName;
            pose.originalBody=Array.Empty<Renderer>();pose.weaponRenderers=weaponMeshes;pose.sourceClips=clips;pose.transferFingers=true;
            var limbs=new List<FrankPoseRetarget.Limb>();
            foreach(string side in new[]{"Left","Right"})foreach(bool arm in new[]{true,false})
            {
                string a=side+(arm?"UpperArm":"UpperLeg"),b=side+(arm?"LowerArm":"LowerLeg"),c=side+(arm?"Hand":"Foot");
                var limb=new FrankPoseRetarget.Limb{sourceUpper=sb(a),sourceMiddle=sb(b),sourceEnd=sb(c),upper=tb(a),middle=tb(b),end=tb(c),middleRest=tb(b).localPosition,endRest=tb(c).localPosition,rotationOffset=Quaternion.Inverse(sb(c).rotation)*tb(c).rotation};
                if(arm)
                {
                    var sm=sb(side+" Middle Proximal");var sr=sb(side+" Ring Proximal");var tm=tb(side+" Middle Proximal");var tr=tb(side+" Ring Proximal");
                    Quaternion sf=Quaternion.LookRotation(sm.position-sb(c).position,Vector3.Cross(sm.position-sb(c).position,sr.position-sm.position));
                    Quaternion tf=Quaternion.LookRotation(tm.position-tb(c).position,Vector3.Cross(tm.position-tb(c).position,tr.position-tm.position));
                    limb.rotationOffset=Quaternion.Inverse(sb(c).rotation)*sf*Quaternion.Inverse(tf)*tb(c).rotation;
                    limb.sourceKnuckle=sm;limb.sourceFingerJoint=sb(side+" Middle Intermediate");limb.targetKnuckle=tm;limb.targetFingerJoint=tb(side+" Middle Intermediate");
                }
                limbs.Add(limb);
            }
            pose.limbs=limbs.ToArray();
            pose.fingers=(from side in new[]{"Left","Right"} from digit in new[]{"Thumb","Index","Middle","Ring","Little"} let p=side+" "+digit+" " where target.avatar.humanDescription.human.Any(h=>h.humanName==p+"Proximal") select new FrankPoseRetarget.Finger{sourceProximal=sb(p+"Proximal"),sourceIntermediate=sb(p+"Intermediate"),sourceDistal=sb(p+"Distal"),proximal=tb(p+"Proximal"),intermediate=tb(p+"Intermediate"),distal=tb(p+"Distal")}).ToArray();
            var driver=root.AddComponent<FrankTestDriver>();driver.pose=pose;driver.characterScale=target.transform.localScale;driver.hips=PathOf(pose.targetHips,target.transform);
            driver.targetLimbs=pose.limbs.Select(l=>new FrankTestDriver.TargetLimb{upper=PathOf(l.upper,target.transform),middle=PathOf(l.middle,target.transform),end=PathOf(l.end,target.transform),knuckle=PathOf(l.targetKnuckle,target.transform),joint=PathOf(l.targetFingerJoint,target.transform)}).ToArray();
            driver.targetFingers=pose.fingers.Select(f=>new FrankTestDriver.TargetFinger{proximal=PathOf(f.proximal,target.transform),intermediate=PathOf(f.intermediate,target.transform),distal=PathOf(f.distal,target.transform)}).ToArray();
            Object.DestroyImmediate(target.gameObject);
            root.name=actor.characterName+"_"+label;
            var saved=PrefabUtility.SaveAsPrefabAsset(root,output+"/Drivers/"+root.name+".prefab").GetComponent<FrankTestDriver>();
            Object.DestroyImmediate(root);return saved;
        }
        public static void BuildVol10()
        {
            var source=EditorSceneManager.OpenPreviewScene(Vol10Scene);
            AnimationClip[] originals;
            try{originals=source.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Animator>()).SelectMany(a=>a.runtimeAnimatorController.animationClips).Distinct().ToArray();}
            finally{EditorSceneManager.ClosePreviewScene(source);}
            var clips=new Dictionary<string,AnimationClip>();
            foreach(var original in originals)
            {
                string path=Vol10Output+"/Clips/"+original.name+".anim";
                if(!File.Exists(path)){var copy=Object.Instantiate(original);copy.name=original.name;AssetDatabase.CreateAsset(copy,path);}
                clips.Add(original.name,AssetDatabase.LoadAssetAtPath<AnimationClip>(path));
            }
            if(clips.Count!=35)throw new Exception("Expected all 35 Vol10 clips, got "+clips.Count);
            var scene=EditorSceneManager.OpenScene(Output+"/Frank_Damages_Mankey_Pepe.unity",OpenSceneMode.Single);
            var tester=Object.FindFirstObjectByType<FrankCombinationTester>();
            tester.mankey.Clear();tester.pepe.Clear();
            tester.mankey.unarmedDriver=MakeVol10Driver(tester.mankey,clips.Values.ToArray(),out float scale);
            tester.pepe.unarmedDriver=MakeVol10Driver(tester.pepe,clips.Values.ToArray(),out float otherScale);
            if(Mathf.Abs(scale-otherScale)>.001f)throw new Exception("Character source scales differ: "+scale+" / "+otherScale);
            string libPath=Vol10Output+"/UnarmedLibrary.asset";
            var library=AssetDatabase.LoadAssetAtPath<FrankUnarmedLibrary>(libPath);
            if(!library){library=ScriptableObject.CreateInstance<FrankUnarmedLibrary>();AssetDatabase.CreateAsset(library,libPath);}
            string[] ids={"Idle","SEOI","BRAIN","CmnAtemi","CmnAtemi2","CmnAtemi3","FISH","FLANK","HOLD","HOLD_LALI","JPBOM","LC_DSCREW","NAGE_ESC","NECK_BREAK","SIHO","B_FUSHA","GS1","GSWING"};
            string[] names={"Idle","Shoulder throw","Brainbuster","Atemi 1","Atemi 2","Atemi 3","Fisherman suplex","Flank throw","Hold","Hold + lariat","Powerbomb","Dragon screw","Throw escape","Neck breaker","Four-way throw","Windmill throw","German suplex","Giant swing"};
            library.pairs=ids.Select((id,i)=>new FrankUnarmedLibrary.Pair{label=names[i],sourceName=id,attacker=clips[id=="Idle"?id:id+"_N"],receiver=clips[id=="Idle"?id:id+"_Y"],receiverOffset=new Vector3(0,0,.9f*scale),receiverRotation=Quaternion.Euler(0,id=="B_FUSHA"||id=="GS1"?0:180,0)}).ToArray();
            EditorUtility.SetDirty(library);tester.unarmedLibrary=library;
            tester.pairSpacing=AssetDatabase.LoadAssetAtPath<FrankPairSpacing>(Vol10Output+"/BodySpacing.asset");
            tester.gunSword=false;tester.unarmed=true;tester.unarmedMotion=1;tester.paused=false;tester.Configure();
            tester.demoCamera.rect=new Rect(0,0,1,1);
            EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssetIfDirty(library);
            File.WriteAllText("Temp/FrankRetarget/vol10-build.txt","35 clips / 18 pairs; common source scale="+scale);
        }
        public static void InspectVol10()
        {
            var scene=EditorSceneManager.OpenPreviewScene(Vol10Scene);var text=new StringBuilder();
            try
            {
                foreach(var root in scene.GetRootGameObjects())
                {
                    text.AppendLine($"ROOT {root.name} pos={root.transform.position} rot={root.transform.rotation.eulerAngles} scale={root.transform.localScale}");
                    foreach(var a in root.GetComponentsInChildren<Animator>(true))
                    {
                        text.AppendLine($"Animator {a.name} avatar={a.avatar} human={a.isHuman} rootmotion={a.applyRootMotion}");
                        if(a.runtimeAnimatorController)foreach(var clip in a.runtimeAnimatorController.animationClips.Distinct())
                        {
                            text.AppendLine($"CLIP {clip.name} duration={clip.length} human={clip.humanMotion} loop={clip.isLooping} asset={AssetDatabase.GetAssetPath(clip)}");
                            var bindings=AnimationUtility.GetCurveBindings(clip);
                            text.AppendLine("ROOT PATHS "+string.Join(";",bindings.Where(b=>b.propertyName.Contains("Position")).Select(b=>b.path).Distinct().Take(5)));
                        }
                        foreach(var t in a.GetComponentsInChildren<Transform>().Where(t=>t.name.Contains("Hips")||t.name.Contains("Reference")))text.AppendLine($"BONE {PathOf(t,a.transform)} pos={t.position} local={t.localPosition}");
                    }
                }
                File.WriteAllText("Temp/FrankRetarget/vol10-inventory.txt",text.ToString());
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
