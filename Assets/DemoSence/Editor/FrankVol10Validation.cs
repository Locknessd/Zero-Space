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
        public static void SaveVol10Default()
        {
            var scene=EditorSceneManager.OpenScene(Output+"/Frank_Damages_Mankey_Pepe.unity",OpenSceneMode.Single);
            var t=Object.FindAnyObjectByType<FrankCombinationTester>();
            t.gunSword=false;t.unarmed=true;t.unarmedMotion=1;t.paused=false;t.loop=true;t.speed=1;t.Configure();
            t.demoCamera.rect=new Rect(0,0,1,1);EditorSceneManager.SaveScene(scene);
        }
        [MenuItem("Tools/Frank Retarget/Validate Vol10 bare-hand library")]
        public static void ValidateVol10()
        {
            string folder=Output+"/Reports/Vol10";Directory.CreateDirectory(folder);
            var scene=EditorSceneManager.OpenPreviewScene(Output+"/Frank_Damages_Mankey_Pepe.unity");
            var t=scene.GetRootGameObjects().Select(g=>g.GetComponent<FrankCombinationTester>()).First(x=>x);
            var m=t.mankey.character;var p=t.pepe.character;
            var originals=AssetDatabase.FindAssets("t:AnimationClip",new[]{Vol10Source}).Select(AssetDatabase.GUIDToAssetPath).Distinct().SelectMany(path=>AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()).Where(c=>!c.name.StartsWith("__preview")).GroupBy(c=>c.name).ToDictionary(g=>g.Key,g=>g.First());
            var reference=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Vol10Source+"/Models/unitychan.fbx"));
            foreach(var r in reference.GetComponentsInChildren<Renderer>())r.enabled=false;
            var csv=new StringBuilder("role,pair,character,frames,hipError,contactError,headFootError,sourceTrajectoryError,neckBend\n");
            int samples=0;float hipError=0,contactError=0,rotationError=0,trajectoryError=0,neck=0,seekDrift=0;
            try
            {
                if(t.unarmedLibrary.pairs.Length!=18)throw new Exception("Missing pairs");
                int savedMotion=t.motion,savedWeapon=t.attackerWeapon,savedReceiver=t.receiverWeapon;
                t.SelectLibrary(true);
                for(int role=0;role<2;role++)for(int motion=0;motion<18;motion++)
                {
                    t.pepeAttacks=role==1;t.unarmedMotion=motion;t.Configure();
                    if(t.mankey.character!=m || t.pepe.character!=p)throw new Exception("Characters replaced");
                    foreach(var actor in new[]{t.Attacker,t.Receiver})
                    {
                        var pose=actor.Pose;
                        if(actor.equipped || pose.weaponRenderers.Length!=0 || actor.activeDriver.GetComponentsInChildren<Renderer>(true).Length!=0)throw new Exception("Unarmed driver has meshes or equipment");
                        reference.transform.SetPositionAndRotation(actor.transform.position,actor.transform.rotation);
                        reference.transform.localScale=actor.activeDriver.transform.localScale;
                        var sourceMap=Bones(reference.transform);
                        var endpoints=pose.limbs.Select(l=>l.sourceEnd).Append(pose.sourceHips).ToArray();
                        float h=0,c=0,r=0,path=0,n=0;
                        int count=Mathf.CeilToInt(t.Duration*60)+1;
                        for(int f=0;f<count;f++)
                        {
                            float time=Mathf.Min(f/60f,t.Duration);actor.Evaluate(time);
                            originals[actor.clip.name].SampleAnimation(reference,Mathf.Min(time,actor.clip.length));
                            h=Mathf.Max(h,Vector3.Distance(pose.sourceHips.position,pose.targetHips.position));
                            foreach(var limb in pose.limbs)c=Mathf.Max(c,Vector3.Distance(limb.end.position,limb.Goal));
                            foreach(var b in pose.Posture.cervical.Where(b=>b.name=="Head").Concat(pose.Posture.toes))r=Mathf.Max(r,RotationAngle(b.Goal,b.target.rotation));
                            foreach(var b in endpoints)path=Mathf.Max(path,Vector3.Distance(b.position,sourceMap[b.name].position));
                            n=Mathf.Max(n,pose.Posture.NeckBend);
                            foreach(var b in actor.character.GetComponentsInChildren<Transform>())if(!float.IsFinite(b.position.sqrMagnitude)||!float.IsFinite(b.rotation.w))throw new Exception("Nonfinite pose");
                            samples++;
                        }
                        var before=actor.character.GetComponentsInChildren<Transform>().Select(b=>b.rotation).ToArray();
                        actor.Evaluate(0);actor.Evaluate(actor.clip.length*.3f);actor.Evaluate(t.Duration);
                        var after=actor.character.GetComponentsInChildren<Transform>();
                        for(int i=0;i<after.Length;i++)seekDrift=Mathf.Max(seekDrift,RotationAngle(before[i],after[i].rotation));
                        csv.AppendLine($"{role},{t.unarmedLibrary.pairs[motion].sourceName},{actor.characterName},{count},{h:R},{c:R},{r:R},{path:R},{n:R}");
                        hipError=Mathf.Max(hipError,h);contactError=Mathf.Max(contactError,c);rotationError=Mathf.Max(rotationError,r);trajectoryError=Mathf.Max(trajectoryError,path);neck=Mathf.Max(neck,n);
                    }
                    t.SwapRoles();t.Restart();if(t.time!=0)throw new Exception("Restart failed");
                }
                t.SelectLibrary(false);
                if(t.motion!=savedMotion||t.attackerWeapon!=savedWeapon||t.receiverWeapon!=savedReceiver)throw new Exception("Weapon library selections lost");
                if(t.Attacker.activeDriver.pose.sourceHips.name.StartsWith("Character1"))throw new Exception("Library switch did not restore Frank");
                bool pass=hipError<.001f&&contactError<.025f&&rotationError<.1f&&trajectoryError<.001f&&neck<=65.01f&&seekDrift<.1f;
                string report=$"Passed: {pass}\n18 pairs, 35 unique clips, both role assignments, two persistent characters.\n60 Hz actor poses: {samples}\nPelvis error: {hipError:R} m\nHand/ankle contact error: {contactError:R} m\nHead/toe orientation error: {rotationError:R} degrees\nOriginal Generic source trajectory error: {trajectoryError:R} m\nMaximum softened neck bend: {neck:R} degrees\nReverse-seek rotation drift: {seekDrift:R} degrees\nWeapons absent in all unarmed pairs. Original library selection retained on switching.\n";
                File.WriteAllText(folder+"/validation.txt",report);File.WriteAllText(folder+"/validation.csv",csv.ToString());
                if(!pass)throw new Exception(report);
            }
            finally{Object.DestroyImmediate(reference);t.mankey.Clear();t.pepe.Clear();EditorSceneManager.ClosePreviewScene(scene);}
        }
        [MenuItem("Tools/Frank Retarget/Check Vol10 in Play mode")]
        public static void Vol10PlayCheck(){FrankVol10PlayCheck.Start();}
    }
    [InitializeOnLoad]
    public static class FrankVol10PlayCheck
    {
        const string Key="FrankVol10PlayCheck";
        static int step,frames,last=-1;static double started;
        static readonly int[] motions={1,2,10,16,17,1,2,10,16,17};
        static FrankVol10PlayCheck(){EditorApplication.update+=Tick;}
        public static void Start()
        {
            Directory.CreateDirectory("Assets/DemoSence/Reports/Vol10");
            EditorSceneManager.OpenScene(FrankRetargetBuilder.Output+"/Frank_Damages_Mankey_Pepe.unity",OpenSceneMode.Single);
            var view=EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView"));view.Show();view.Focus();view.maximized=true;
            SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;
        }
        static void Tick()
        {
            if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
            try
            {
                Application.runInBackground=true;
                var t=Object.FindAnyObjectByType<FrankCombinationTester>();if(!t||!t.Attacker.activeDriver)return;
                if(started==0)started=EditorApplication.timeSinceStartup;
                if(EditorApplication.timeSinceStartup-started>180)throw new Exception("Timeout");
                if(last==Time.frameCount)return;last=Time.frameCount;
                if(frames==0)
                {
                    if(step<motions.Length)
                    {t.paused=true;t.gunSword=false;t.unarmed=true;t.pepeAttacks=step>=5;t.unarmedMotion=motions[step];t.Configure();t.Seek(t.Duration*.48f);}
                    else if(step==10){t.Restart();if(t.time!=0)throw new Exception("Restart");t.speed=2;t.loop=true;t.Seek(t.Duration-.001f);t.paused=false;}
                    else if(step==11){if(t.time>=t.Duration)throw new Exception("Loop");t.loop=false;t.Seek(t.Duration-.001f);t.paused=false;}
                    else if(step==12){if(!t.paused||t.time!=t.Duration)throw new Exception("End hold");t.SelectLibrary(false);t.motion=1;t.attackerWeapon=4;t.Configure();t.Seek(5.29f);}
                    else {Finish(null);return;}
                }
                if(++frames<8)return;
                if(step<10)ScreenCapture.CaptureScreenshot($"Assets/DemoSence/Reports/Vol10/{t.Attacker.characterName}-{t.unarmedLibrary.pairs[t.unarmedMotion].sourceName}.png");
                else if(step==12)ScreenCapture.CaptureScreenshot("Assets/DemoSence/Reports/Vol10/Frank-library.png");
                step++;frames=-4;
            }
            catch(Exception e){Finish(e.ToString());}
        }
        static void Finish(string error)
        {
            File.WriteAllText("Assets/DemoSence/Reports/Vol10/play-mode.txt",$"Passed: {error==null}\nLive captured poses on both characters; restart, speed, looping, end hold and switching back to Frank.\n{error}");
            SessionState.SetBool(Key,false);EditorApplication.isPlaying=false;
            EditorApplication.delayCall+=()=>EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).maximized=false;
            step=frames=0;last=-1;started=0;
        }
    }
}
