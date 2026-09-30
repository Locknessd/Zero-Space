using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public const string Output = "Assets/DemoSence";
        public const string Original = "Assets/Selected/Frank_Damages/Demo/Frank_Damages.unity";
        const string Source = "Assets/Selected/Frank_Damages/Asset";
        public static readonly string[] Weapons = {"2Handed", "Assassin", "Dual", "GreatSword", "Katana", "Spear", "Warrior"};
        static void Folder(string path) { if(Directory.Exists(path))return; Directory.CreateDirectory(path); AssetDatabase.Refresh(); }
        static string Copy(string from, string to)
        {
            if (!File.Exists(to) && !AssetDatabase.CopyAsset(from, to)) throw new Exception("Cannot copy " + from);
            return to;
        }
        [MenuItem("Tools/Frank Retarget/Legacy/1. Prepare derived rigs")]
        public static void Prepare()
        {
            Folder(Output + "/Rigs"); Folder(Output + "/SourceMotion");
            var sb = new StringBuilder();
            foreach(var name in Weapons.Concat(new[]{"FS3_Skin"}))
            {
                string file = name == "FS3_Skin" ? "Frank_Damage_FS3_Skin.FBX" : "Frank_Damage@Damage_Critical_"+name+".FBX";
                string path = Copy(Source+"/Meshes/"+file, Output+"/Rigs/"+file);
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.optimizeGameObjects = false;
                importer.SaveAndReimport();
                var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().First();
                var description=avatar.humanDescription;
                int hips=Array.FindIndex(description.human,b=>b.humanName=="Hips");
                if(hips>=0 && description.human[hips].boneName!="pelvis")
                {
                    description.human[hips].boneName="pelvis";
                    importer.humanDescription=description;importer.SaveAndReimport();
                    avatar=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().First();
                }
                sb.AppendLine(name+" avatar valid="+avatar.isValid+" human="+avatar.isHuman+" bones="+avatar.humanDescription.human.Length);
                if(!avatar.isValid || !avatar.isHuman) throw new Exception("Invalid source avatar: "+path);
            }
            foreach(var name in Weapons.Concat(new[]{"Warrior_Hit2"}))
            {
                string suffix = name == "Warrior_Hit2" ? name : name+"_Hit";
                string file = "Frank_Damage@Damage_Critical_"+suffix+".FBX";
                string path = Copy(Source+"/Animations/Damages_Critical_Set/"+file, Output+"/SourceMotion/"+file);
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.sourceAvatar = null;
                importer.optimizeGameObjects = false;
                importer.animationCompression = ModelImporterAnimationCompression.Off;
                importer.motionNodeName = "";
                importer.SaveAndReimport();
                foreach(var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview")))
                    sb.AppendLine(suffix+" clip="+clip.name+" duration="+clip.length+" curves="+AnimationUtility.GetCurveBindings(clip).Length+" human="+clip.humanMotion);
            }
            foreach(var item in new[]{new[]{"Assets/Mankeys_sushishop/FBX/Mankey.FBX","Mankey.FBX"},new[]{"Assets/pepe/source/pepFBX@Taunt.fbx","Pepe.fbx"}})
            {
                string path=Copy(item[0],Output+"/Rigs/"+item[1]);
                var imp=(ModelImporter)AssetImporter.GetAtPath(path);
                imp.optimizeGameObjects=false;
                imp.SaveAndReimport();
                var av=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().First();
                sb.AppendLine(item[1]+" valid="+av.isValid+" human="+av.isHuman);
                if(!av.isValid||!av.isHuman)throw new Exception("Invalid target avatar "+path);
            }
            File.WriteAllText("Temp/FrankRetarget/preparation.txt",sb.ToString());
        }

        static Avatar AvatarAt(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().First();
        static Dictionary<string,Transform> Bones(Transform root) => root.GetComponentsInChildren<Transform>(true).GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
        static void ReferencePose(Transform root, Avatar avatar)
        {
            var bones=Bones(root);
            foreach(var b in avatar.humanDescription.skeleton)
                if(bones.TryGetValue(b.name,out var t) && t!=root)
                {t.localPosition=b.position;t.localRotation=b.rotation;t.localScale=b.scale;}
        }
        static Transform HumanBone(Transform root, Avatar avatar, string name)
        {
            string bone=avatar.humanDescription.human.First(h=>h.humanName==name).boneName;
            return Bones(root)[bone];
        }
        static AnimatorController DerivedController(AnimatorController source, string character, string role)
        {
            string folder=Output+"/Sets/"+character+"/"+role;
            Folder(folder);
            string path=Copy(AssetDatabase.GetAssetPath(source),folder+"/"+source.name+".controller");
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            // Keep the disconnected alternate reaction available without changing the
            // original default sequence or exit transitions.
            if(source.name=="Critical_Warrior_Hit")
            {
                var machine=controller.layers[0].stateMachine;
                var alternate=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimatorState>().FirstOrDefault(s=>s.name=="Damage_Critical_Warrior_Hit2");
                if(alternate && !machine.states.Any(s=>s.state==alternate))machine.AddState(alternate,new Vector3(650,250));
            }
            foreach(var layer in controller.layers) ReplaceMotions(layer.stateMachine,folder);
            EditorUtility.SetDirty(controller);
            return controller;
        }
        static void ReplaceMotions(AnimatorStateMachine machine,string folder)
        {
            foreach(var child in machine.states)
            {
                var clip=child.state.motion as AnimationClip;
                if(!clip || (child.state.name=="Damage_Critical_Warrior_Hit2" && clip.name!=child.state.name))
                {
                    // The supplied Warrior_Hit2 state points to the first reaction take.
                    // Resolve its authored take by state name in the derived controller only.
                    string missing=Output+"/SourceMotion/Frank_Damage@"+child.state.name+".FBX";
                    clip=AssetDatabase.LoadAllAssetsAtPath(missing).OfType<AnimationClip>().FirstOrDefault(c=>!c.name.StartsWith("__preview"));
                    if(!clip)throw new Exception("Unresolved state motion: "+child.state.name);
                }
                // Preserve the imported authored transform tracks for reactions. The supplied
                // scene mistakenly runs these Humanoid clips on a Generic FS3 avatar.
                string clipPath=folder+"/"+clip.name+".anim";
                if(!File.Exists(clipPath))
                {
                    var original=clip;
                    if(clip.humanMotion)
                    {
                        string raw=Output+"/SourceMotion/Frank_Damage@"+clip.name+".FBX";
                        clip=AssetDatabase.LoadAllAssetsAtPath(raw).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview"));
                    }
                    var copy=Object.Instantiate(clip); copy.name=original.name;
                    var settings=AnimationUtility.GetAnimationClipSettings(original);
                    // Generic bone curves carry the complete authored path, including height.
                    // Never extract it into a second Animator root-motion accumulator.
                    settings.startTime=0; settings.stopTime=original.length;
                    AnimationUtility.SetAnimationClipSettings(copy,settings);
                    AnimationUtility.SetAnimationEvents(copy,AnimationUtility.GetAnimationEvents(original));
                    AssetDatabase.CreateAsset(copy,clipPath);
                }
                child.state.motion=AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                EditorUtility.SetDirty(child.state);
            }
            foreach(var child in machine.stateMachines)ReplaceMotions(child.stateMachine,folder);
        }
        static Material MaterialFor(Material source,string character)
        {
            string name=character+"_"+source.name.Replace('/','_');
            string path=Output+"/Materials/"+name+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(mat)return mat;
            mat=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name};
            Texture texture=source.HasProperty("_BaseMap")?source.GetTexture("_BaseMap"):source.mainTexture;
            Color color=source.HasProperty("_BaseColor")?source.GetColor("_BaseColor"):source.HasProperty("_Color")?source.color:Color.white;
            if(character=="Pepe") {texture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/pepe/textures/pep_BaseColor.png");color=Color.white;}
            mat.SetTexture("_BaseMap",texture);mat.SetColor("_BaseColor",color);
            mat.SetFloat("_Smoothness",0.25f);
            if(source.HasProperty("_BumpMap") && source.GetTexture("_BumpMap"))
            {mat.SetTexture("_BumpMap",source.GetTexture("_BumpMap"));mat.EnableKeyword("_NORMALMAP");}
            AssetDatabase.CreateAsset(mat,path);return mat;
        }
        public static void CorrectKatanaAvatar()
        {
            string path=Output+"/Rigs/Frank_Damage@Damage_Critical_Katana.FBX";
            var avatar=AvatarAt(path);
            var description=avatar.humanDescription;
            File.WriteAllText("Temp/FrankRetarget/katana-mapping.txt",string.Join("\n",description.human.Select(b=>b.humanName+"="+b.boneName)));
            for(int i=0;i<description.human.Length;i++)
                if(description.human[i].humanName=="Hips")description.human[i].boneName="pelvis";
            var importer=(ModelImporter)AssetImporter.GetAtPath(path);
            importer.humanDescription=description;importer.SaveAndReimport();
        }
        static FrankPoseRetarget MakeActor(GameObject root,string character,string weapon,bool attacker)
        {
            if(PrefabUtility.IsPartOfPrefabInstance(root))PrefabUtility.UnpackPrefabInstance(root,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            var driver=root.GetComponent<Animator>();
            var sourceAvatar=AvatarAt(Output+"/Rigs/"+(attacker?"Frank_Damage@Damage_Critical_"+weapon:"Frank_Damage_FS3_Skin")+".FBX");
            ReferencePose(root.transform,sourceAvatar);
            driver.runtimeAnimatorController=DerivedController((AnimatorController)driver.runtimeAnimatorController,character,attacker?"Hit":"Being Hit");
            driver.applyRootMotion=false;driver.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            var renders=root.GetComponentsInChildren<Renderer>(true);
            var body=renders.Where(r=>r.name.StartsWith("Frank_")||r.name=="Cloak"||r.name=="Cape"||r.name=="Mesh_Pants").ToArray();
            foreach(var r in body)r.enabled=false;
            foreach(var sk in renders.OfType<SkinnedMeshRenderer>())sk.updateWhenOffscreen=true;
            var target=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Output+"/Rigs/"+(character=="Mankey"?"Mankey.FBX":"Pepe.fbx")));
            target.name=character;
            target.transform.SetParent(root.transform,false);
            var animator=target.GetComponent<Animator>();
            animator.runtimeAnimatorController=null; animator.applyRootMotion=false;animator.enabled=false;
            ReferencePose(target.transform,animator.avatar);
            Func<Transform,Avatar,string,Transform> bone=HumanBone;
            float sourceLeg=Vector3.Distance(bone(root.transform,sourceAvatar,"LeftUpperLeg").position,bone(root.transform,sourceAvatar,"LeftLowerLeg").position)+Vector3.Distance(bone(root.transform,sourceAvatar,"LeftLowerLeg").position,bone(root.transform,sourceAvatar,"LeftFoot").position);
            float targetLeg=Vector3.Distance(animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position,animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position)+Vector3.Distance(animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position,animator.GetBoneTransform(HumanBodyBones.LeftFoot).position);
            target.transform.localScale*=sourceLeg/targetLeg;
            foreach(var r in target.GetComponentsInChildren<Renderer>(true))
            {
                r.sharedMaterials=r.sharedMaterials.Select(m=>MaterialFor(m,character)).ToArray();
                if(r is SkinnedMeshRenderer sk)sk.updateWhenOffscreen=true;
                if(r.name.IndexOf("coconut",StringComparison.OrdinalIgnoreCase)>=0)r.enabled=false;
            }
            var bridge=root.AddComponent<FrankPoseRetarget>();
            bridge.driver=driver;bridge.sourceHumanAvatar=sourceAvatar;bridge.character=animator;
            bridge.characterName=character;bridge.weapon=weapon;bridge.isAttacker=attacker;
            bridge.originalBody=body;bridge.weaponRenderers=renders.Except(body).ToArray();
            bridge.sourceClips=driver.runtimeAnimatorController.animationClips.Distinct().ToArray();
            bridge.sourceHips=bone(root.transform,sourceAvatar,"Hips");bridge.targetHips=animator.GetBoneTransform(HumanBodyBones.Hips);
            var limbs=new List<FrankPoseRetarget.Limb>();
            foreach(var side in new[]{"Left","Right"})foreach(bool arm in new[]{true,false})
            {
                string a=side+(arm?"UpperArm":"UpperLeg"),b=side+(arm?"LowerArm":"LowerLeg"),c=side+(arm?"Hand":"Foot");
                var sEnd=bone(root.transform,sourceAvatar,c);var tEnd=bone(target.transform,animator.avatar,c);
                var mid=bone(target.transform,animator.avatar,b);
                var limb=new FrankPoseRetarget.Limb{sourceUpper=bone(root.transform,sourceAvatar,a),sourceMiddle=bone(root.transform,sourceAvatar,b),sourceEnd=sEnd,
                    upper=bone(target.transform,animator.avatar,a),middle=mid,end=tEnd,
                    middleRest=mid.localPosition,endRest=tEnd.localPosition,rotationOffset=Quaternion.Inverse(sEnd.rotation)*tEnd.rotation};
                if(arm)
                {
                    var sm=bone(root.transform,sourceAvatar,side+" Middle Proximal");
                    var sr=bone(root.transform,sourceAvatar,side+" Ring Proximal");
                    var tm=bone(target.transform,animator.avatar,side+" Middle Proximal");
                    var tr=bone(target.transform,animator.avatar,side+" Ring Proximal");
                    Quaternion sf=Quaternion.LookRotation(sm.position-sEnd.position,Vector3.Cross(sm.position-sEnd.position,sr.position-sm.position));
                    Quaternion tf=Quaternion.LookRotation(tm.position-tEnd.position,Vector3.Cross(tm.position-tEnd.position,tr.position-tm.position));
                    // Align anatomical palm frames, not the unrelated FBX bone local axes.
                    limb.rotationOffset=Quaternion.Inverse(sEnd.rotation)*sf*Quaternion.Inverse(tf)*tEnd.rotation;
                    limb.alignGrip=attacker;
                    limb.sourceKnuckle=sm;limb.sourceFingerJoint=bone(root.transform,sourceAvatar,side+" Middle Intermediate");
                    limb.targetKnuckle=tm;limb.targetFingerJoint=bone(target.transform,animator.avatar,side+" Middle Intermediate");
                }
                limbs.Add(limb);
            }
            bridge.limbs=limbs.ToArray();
            var fingers=new List<FrankPoseRetarget.Finger>();
            if(attacker)foreach(string side in new[]{"Left","Right"})foreach(string digit in new[]{"Thumb","Index","Middle","Ring","Little"})
            {
                string prefix=side+" "+digit+" ";
                if(!animator.avatar.humanDescription.human.Any(h=>h.humanName==prefix+"Proximal"))continue;
                fingers.Add(new FrankPoseRetarget.Finger{
                    sourceProximal=bone(root.transform,sourceAvatar,prefix+"Proximal"),sourceIntermediate=bone(root.transform,sourceAvatar,prefix+"Intermediate"),sourceDistal=bone(root.transform,sourceAvatar,prefix+"Distal"),
                    proximal=bone(target.transform,animator.avatar,prefix+"Proximal"),intermediate=bone(target.transform,animator.avatar,prefix+"Intermediate"),distal=bone(target.transform,animator.avatar,prefix+"Distal")});
            }
            bridge.fingers=fingers.ToArray();
            bridge.sourceClips[0].SampleAnimation(root,0);
            bridge.ApplyPose();
            root.name=character+" - "+weapon+" - "+(attacker?"Hit":"Being Hit");
            string path=Output+"/Sets/"+character+"/"+(attacker?"Hit":"Being Hit")+"/"+weapon+".prefab";
            PrefabUtility.SaveAsPrefabAsset(root,path);
            return bridge;
        }
        [MenuItem("Tools/Frank Retarget/Legacy/2. Build cloned scene")]
        public static void Build()
        {
            Folder(Output+"/Materials");Folder(Output+"/Reports");
            var active=SceneManager.GetActiveScene();
            var source=EditorSceneManager.OpenPreviewScene(Original);
            string scenePath=Output+"/Frank_Damages_Mankey_Pepe.unity";
            // Begin with an actual scene copy so rendering, lightmapping, navigation and
            // environment settings survive along with the original GameObjects.
            string staging=Output+"/__FrankRetargetBuild.unity";
            File.Copy(Original,staging,true);AssetDatabase.ImportAsset(staging);
            var scene=EditorSceneManager.OpenScene(staging,OpenSceneMode.Additive);
            var previousClone=SceneManager.GetSceneByPath(scenePath);
            if(previousClone.IsValid() && previousClone.isLoaded)EditorSceneManager.CloseScene(previousClone,true);
            SceneManager.SetActiveScene(scene);
            try
            {
                var groups=new GameObject[2];
                var actors=source.GetRootGameObjects().Where(g=>g.name.StartsWith("Frank_Damage")).ToArray();
                foreach(var old in scene.GetRootGameObjects().Where(g=>g.name.StartsWith("Frank_Damage")))Object.DestroyImmediate(old);
                for(int variant=0;variant<2;variant++)
                {
                    groups[variant]=new GameObject(variant==0?"Mankey Hit - Pepe Being Hit":"Pepe Hit - Mankey Being Hit");
                    foreach(var actor in actors)
                    {
                        bool attacker=!actor.name.EndsWith("_Hit");
                        string weapon=Weapons.First(w=>actor.name.Contains("_"+w));
                        string character=(attacker==(variant==0))?"Mankey":"Pepe";
                        var copy=Object.Instantiate(actor);copy.transform.SetParent(groups[variant].transform,true);
                        MakeActor(copy,character,weapon,attacker);
                    }
                    groups[variant].SetActive(variant==0);
                }
                var demo=new GameObject("Demo controls").AddComponent<FrankDemoControls>();
                demo.variants=groups;
                demo.demoCamera=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).First();
                EditorSceneManager.SaveScene(scene,Output+"/Frank_Damages_Mankey_Pepe.unity");
                foreach(string path in Directory.GetFiles(Output,"*.controller",SearchOption.AllDirectories))
                    AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadAssetAtPath<AnimatorController>(path));
            }
            finally
            {
                if(active.IsValid()&&active.isLoaded&&active!=scene)SceneManager.SetActiveScene(active);
                if(SceneManager.sceneCount>1)EditorSceneManager.CloseScene(scene,true);
                EditorSceneManager.ClosePreviewScene(source);
                AssetDatabase.DeleteAsset(staging);
            }
        }
    }
}
