using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void GripPreviews()
        {
            string folder="Temp/FrankRetarget/Grips";Directory.CreateDirectory(folder);
            var clone=EditorSceneManager.OpenPreviewScene(Output+"/Frank_Damages_Mankey_Pepe.unity");
            var original=EditorSceneManager.OpenPreviewScene(Original);
            var sb=new StringBuilder();
            try
            {
                var actors=clone.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<FrankPoseRetarget>(true)).ToArray();
                foreach(var parent in actors.Select(a=>a.transform.parent.gameObject).Distinct())parent.SetActive(true);
                var originals=original.GetRootGameObjects().Where(g=>g.name.StartsWith("Frank_Damage")).ToArray();
                foreach(string weapon in Weapons)
                {
                    var source=originals.First(g=>g.name.Contains("_"+weapon)&&!g.name.EndsWith("_Hit"));
                    foreach(var g in originals)g.SetActive(g==source);
                    source.GetComponent<Animator>().runtimeAnimatorController.animationClips.First().SampleAnimation(source,0);
                    for(int variant=0;variant<3;variant++)
                    {
                        string character=variant==1?"Mankey":"Pepe";
                        var actor=actors.First(a=>a.isAttacker&&a.weapon==weapon&&a.characterName==character);
                        foreach(var other in actors)other.gameObject.SetActive(other==actor);
                        actor.sourceClips[0].SampleAnimation(actor.gameObject,0);actor.ApplyPose();
                        var root=variant==0?source.transform:actor.character.transform;
                        var avatar=variant==0?actor.sourceHumanAvatar:actor.character.avatar;
                        var scene=variant==0?original:clone;
                        var camera=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).First();
                        camera.scene=scene;camera.orthographic=true;camera.orthographicSize=0.24f;
                        foreach(string side in new[]{"Right","Left"})
                        {
                        var hand=HumanBone(root,avatar,side+"Hand");
                        var index=HumanBone(root,avatar,side+" Ring Proximal");
                        var middle=HumanBone(root,avatar,side+" Middle Proximal");
                        var thumb=HumanBone(root,avatar,side+" Thumb Proximal");
                        Vector3 focus=(hand.position+middle.position)*0.5f;
                        var direction=(middle.position-hand.position).normalized;
                        var across=(index.position-middle.position).normalized;
                        var normal=Vector3.Cross(direction,across).normalized;
                        var chest=HumanBone(root,avatar,"Chest");
                        if(Vector3.Dot(normal,focus-chest.position)<0)normal=-normal;
                        camera.transform.position=focus+normal*.7f+direction*.15f;
                        camera.transform.LookAt(focus,Vector3.up);
                        Capture(camera,folder+"/"+weapon+"_"+variant+(side=="Left"?"_Left":"")+".png",640,640);
                        sb.AppendLine(weapon+" "+variant+" "+side+" hand="+hand.position+" middle="+middle.position+" index="+index.position+" thumb="+thumb.position+" normal="+normal);
                        }
                    }
                }
            }
            finally{EditorSceneManager.ClosePreviewScene(clone);EditorSceneManager.ClosePreviewScene(original);}
            File.WriteAllText(folder+"/positions.txt",sb.ToString());
        }
    }
}
