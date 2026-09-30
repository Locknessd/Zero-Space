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
        public static void ValidateInsaneCombos()
        {
            var scene=EditorSceneManager.OpenPreviewScene(Output+"/Frank_Damages_Mankey_Pepe.unity");
            var t=scene.GetRootGameObjects().Select(g=>g.GetComponent<FrankCombinationTester>()).First(x=>x);
            int samples=0;float minFloor=float.PositiveInfinity;float grip=0,neck=0,drift=0,hip=0,ground=0,flat=0,maxStep=0,maxGeneratedStep=0;
            var csv=new StringBuilder("clip,attacker,duration,receiverEndPelvisHeight,receiverEndHeadHeight,receiverTorsoVerticalFraction,maxHandGripError,maxPelvisFrameStep\n");
            var m=t.mankey.character;var p=t.pepe.character;var jumps=new StringBuilder();
            try
            {
                if(scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Renderer>()).Any(r=>r.name=="STYLIZED-BASEMESH-BIGMALE-OBJ"))throw new Exception("Original body still visible in scene");
                if(t.comboLibrary.pairs.Length!=17 || t.comboLibrary.pairs.Count(x=>x.step>0)!=14)throw new Exception("Missing combo steps");
                for(int role=0;role<2;role++)for(int motion=0;motion<17;motion++)
                {
                    t.gunSword=true;t.unarmed=false;t.pepeAttacks=role==1;t.comboMotion=motion;t.Configure();
                    var pair=t.comboLibrary.pairs[motion];
                    var floorSkins=t.Receiver.character.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.enabled).Select(r=>new SkinFloor(r)).ToArray();
                    if(t.Attacker.Pose.weaponRenderers.Count(r=>r.enabled)!=2||t.Receiver.Pose.weaponRenderers.Any(r=>r.enabled))throw new Exception("Equipment setup");
                    if(t.mankey.character!=m||t.pepe.character!=p)throw new Exception("Replaced characters");
                    float localGrip=0,step=0;Vector3 previous=t.Receiver.Pose.targetHips.position;
                    for(int f=0;f<=Mathf.CeilToInt(t.Duration*60);f++)
                    {
                        t.Seek(Mathf.Min(f/60f,t.Duration));
                        foreach(var actor in new[]{t.Attacker,t.Receiver})
                        {
                            var expectedHip=actor.Pose.sourceHips.position+(actor==t.Receiver?Vector3.up*pair.FloorOffset(actor==t.mankey,t.time):Vector3.zero);
                            hip=Mathf.Max(hip,Vector3.Distance(expectedHip,actor.Pose.targetHips.position));
                            neck=Mathf.Max(neck,actor.Pose.Posture.NeckBend);
                            foreach(var limb in actor.Pose.limbs.Where(l=>l.alignGrip))localGrip=Mathf.Max(localGrip,Vector3.Distance(limb.SourceGrip,limb.TargetGrip));
                            foreach(var bone in actor.character.GetComponentsInChildren<Transform>())if(!float.IsFinite(bone.position.sqrMagnitude))throw new Exception("Nonfinite pose");
                        }
                        if(f>0){float delta=Vector3.Distance(previous,t.Receiver.Pose.targetHips.position);step=Mathf.Max(step,delta);if(delta>.15f)jumps.AppendLine($"{pair.sourceName} {t.Receiver.characterName} t={t.time} delta={delta} source={t.Receiver.Pose.sourceHips.position} floor={pair.FloorOffset(t.Receiver==t.mankey,t.time)}");}previous=t.Receiver.Pose.targetHips.position;samples++;
                    }
                    minFloor=Mathf.Min(minFloor,floorSkins.Min(s=>s.Minimum()));
                    var hips=t.Receiver.Pose.targetHips;var head=HumanBone(t.Receiver.character.transform,t.Receiver.character.avatar,"Head");
                    float vertical=Mathf.Abs((head.position-hips.position).normalized.y);
                    ground=Mathf.Max(ground,hips.position.y);flat=Mathf.Max(flat,vertical);
                    var end=hips.position;t.Seek(t.Duration*.2f);t.Seek(0);t.Seek(t.Duration);drift=Mathf.Max(drift,Vector3.Distance(end,hips.position));
                    grip=Mathf.Max(grip,localGrip);maxStep=Mathf.Max(maxStep,step);if(pair.step>0)maxGeneratedStep=Mathf.Max(maxGeneratedStep,step);
                    csv.AppendLine($"{pair.sourceName},{t.Attacker.characterName},{t.Duration:R},{hips.position.y:R},{head.position.y:R},{vertical:R},{localGrip:R},{step:R}");
                }
                bool pass=grip<.03f&&neck<=65.01f&&drift<.001f&&hip<.001f&&ground<.65f&&flat<.5f&&minFloor>=-.002f&&maxGeneratedStep<.22f;
                string report=$"Passed: {pass}\n17 choices: three full combos + 14 generated step reactions; both roles.\nPaired 60 Hz samples: {samples}\nMax weapon grip error: {grip:R} m\nMax neck bend: {neck:R} degrees\nMax pelvis error after landing correction: {hip:R} m\nMinimum endpoint mesh height: {minFloor:R} m\nMax endpoint pelvis height: {ground:R} m\nMax endpoint torso vertical fraction: {flat:R}\nReverse-seek endpoint drift: {drift:R} m\nMax receiver pelvis movement per frame: {maxStep:R} m\nMax generated reaction pelvis movement per frame: {maxGeneratedStep:R} m\nExactly two persistent characters, gun and sword only on attacker.\n";
                File.WriteAllText(Output+"/Reports/InsaneCombos/jumps.txt",jumps.ToString());File.WriteAllText(Output+"/Reports/InsaneCombos/validation.txt",report);File.WriteAllText(Output+"/Reports/InsaneCombos/validation.csv",csv.ToString());
                if(!pass)throw new Exception(report);
            }
            finally{t.mankey.Clear();t.pepe.Clear();EditorSceneManager.ClosePreviewScene(scene);}
        }
        public static void InsanePlayCheck(){FrankInsanePlayCheck.Start();}
    }
    [InitializeOnLoad]
    public static class FrankInsanePlayCheck
    {
        const string Key="FrankInsanePlayCheck";
        static int step,frames,last=-1;static double started;
        static readonly int[] motions={0,1,5,12,1,5,16};
        static FrankInsanePlayCheck(){EditorApplication.update+=Tick;}
        public static void Start()
        {
            step=frames=0;last=-1;started=0;
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
                    if(step<motions.Length*2)
                    {
                        int index=step/2;t.paused=true;t.gunSword=true;t.unarmed=false;t.pepeAttacks=index>=4;t.comboMotion=motions[index];t.Configure();
                        var pair=t.comboLibrary.pairs[t.comboMotion];t.Seek(step%2==1?t.Duration:pair.step==0?t.Duration*.45f:pair.impactTime+.1f);
                    }
                    else if(step==motions.Length*2){t.Restart();if(t.time!=0)throw new Exception("Restart");t.speed=2;t.loop=true;t.Seek(t.Duration-.001f);t.paused=false;}
                    else if(step==motions.Length*2+1){if(t.time>=t.Duration)throw new Exception("Loop");t.loop=false;t.Seek(t.Duration-.001f);t.paused=false;}
                    else if(step==motions.Length*2+2){if(!t.paused||t.time!=t.Duration)throw new Exception("End hold");t.SelectLibrary(true);if(t.Attacker.Pose.weaponRenderers.Any(r=>r.enabled))throw new Exception("Vol10 equipment");t.SelectLibrary(false);}
                    else{Finish(null);return;}
                }
                if(++frames<3)return;
                if(step<motions.Length*2)ScreenCapture.CaptureScreenshot($"Assets/DemoSence/Reports/InsaneCombos/{t.Attacker.characterName}-{t.comboLibrary.pairs[t.comboMotion].sourceName}-{(step%2==1?"ground":"impact")}.png");
                step++;frames=-2;
            }
            catch(Exception e){Finish(e.ToString());}
        }
        static void Finish(string error)
        {
            File.WriteAllText("Assets/DemoSence/Reports/InsaneCombos/play-mode.txt",$"Passed: {error==null}\nLive impacts and ground finishes, both characters; restart, speed, loop, hold, switching libraries.\n{error}");
            SessionState.SetBool(Key,false);EditorApplication.isPlaying=false;
            EditorApplication.delayCall+=()=>EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).maximized=false;
            step=frames=0;last=-1;started=0;
        }
    }
}
