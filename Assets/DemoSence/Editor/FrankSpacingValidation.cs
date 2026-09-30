using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void ValidateBodySpacing()
        {
            var scene=EditorSceneManager.OpenPreviewScene(Output+"/Frank_Damages_Mankey_Pepe.unity");
            var t=scene.GetRootGameObjects().Select(g=>g.GetComponent<FrankCombinationTester>()).First(x=>x);
            var mankey=t.mankey.character;var pepe=t.pepe.character;
            float before=0,after=0,feet=0,solve=0,seek=0,sourceDrift=0,neck=0;int frames=0;
            var report=new StringBuilder("motion,attacker,meanOverlapBefore,meanOverlapAfter,maxPlantedFootError,maxLimbSolveError\n");
            try
            {
                t.gunSword=false;t.unarmed=true;t.bodySpacing=1;
                for(int motion=0;motion<18;motion++)for(int role=0;role<2;role++)
                {
                    t.unarmedMotion=motion;t.pepeAttacks=role==1;t.Configure();
                    var va=role==0?t.pairSpacing.mankey:t.pairSpacing.pepe;var vb=role==0?t.pairSpacing.pepe:t.pairSpacing.mankey;
                    int count=Mathf.CeilToInt(t.Duration*60)+1;float bsum=0,asum=0,fmax=0,smax=0;
                    for(int frame=0;frame<count;frame++)
                    {
                        t.time=t.Duration*frame/(count-1);
                        t.Attacker.Evaluate(t.time);t.Receiver.Evaluate(t.time);
                        var a=Centers(t.Attacker,va);var b=Centers(t.Receiver,vb);
                        float overlap=Penetration(a,b,va,vb,Vector3.zero);bsum+=overlap;before+=overlap;
                        var sourceA=t.Attacker.Pose.sourceHips.position;var sourceB=t.Receiver.Pose.sourceHips.position;
                        t.Evaluate();
                        overlap=Penetration(Centers(t.Attacker,va),Centers(t.Receiver,vb),va,vb,Vector3.zero);asum+=overlap;after+=overlap;
                        sourceDrift=Mathf.Max(sourceDrift,Vector3.Distance(sourceA,t.Attacker.Pose.sourceHips.position),Vector3.Distance(sourceB,t.Receiver.Pose.sourceHips.position));
                        foreach(var actor in new[]{t.Attacker,t.Receiver})
                        {
                            if(actor.equipped)throw new Exception("Equipment in unarmed move");
                            foreach(var limb in actor.Pose.limbs)
                            {
                                smax=Mathf.Max(smax,limb.error);
                                if(!limb.sourceKnuckle && limb.sourceEnd.position.y<=.16f)fmax=Mathf.Max(fmax,Vector3.Distance(limb.end.position,limb.Goal));
                            }
                            neck=Mathf.Max(neck,actor.Pose.Posture.NeckBend);
                            foreach(var bone in actor.character.GetComponentsInChildren<Transform>())if(!float.IsFinite(bone.position.sqrMagnitude))throw new Exception("Nonfinite pose");
                        }
                        frames++;
                    }
                    var expected=new[]{t.Attacker.Pose.targetHips.position,t.Receiver.Pose.targetHips.position};
                    t.Seek(0);t.Seek(t.Duration*.3f);t.Seek(t.Duration);
                    seek=Mathf.Max(seek,Vector3.Distance(expected[0],t.Attacker.Pose.targetHips.position),Vector3.Distance(expected[1],t.Receiver.Pose.targetHips.position));
                    feet=Mathf.Max(feet,fmax);solve=Mathf.Max(solve,smax);
                    report.AppendLine($"{t.unarmedLibrary.pairs[motion].sourceName},{t.Attacker.characterName},{bsum/count:R},{asum/count:R},{fmax:R},{smax:R}");
                    if(t.mankey.character!=mankey||t.pepe.character!=pepe)throw new Exception("Characters changed");
                }
                t.SelectLibrary(false);t.Seek(.5f);
                foreach(var actor in new[]{t.Attacker,t.Receiver})if(Vector3.Distance(actor.Pose.targetHips.position,actor.Pose.sourceHips.position)>.001f)throw new Exception("Spacing leaked into weapon library");
                bool pass=after<before*.35f && feet<.001f && solve<.035f && seek<.001f && sourceDrift<.001f && neck<=65.01f;
                string result=$"Passed: {pass}\nPaired 60 Hz frames: {frames}\n18 pairs, both role assignments, same two characters.\nMean body-volume penetration before: {before/frames:R} m\nMean body-volume penetration after: {after/frames:R} m\nReduction: {(1-after/before)*100:R}%\nMax planted ankle drift: {feet:R} m\nMax limb solve error: {solve:R} m\nReverse-seek pelvis drift: {seek:R} m\nOriginal source trajectory drift: {sourceDrift:R} m\nMax neck bend: {neck:R} degrees\nNo equipment in unarmed clips. Weapon library pelvis path unaffected.\n";
                File.WriteAllText(Output+"/Reports/Spacing/validation.txt",result);File.WriteAllText(Output+"/Reports/Spacing/validation.csv",report.ToString());
                if(!pass)throw new Exception(result);
            }
            finally{t.mankey.Clear();t.pepe.Clear();EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
