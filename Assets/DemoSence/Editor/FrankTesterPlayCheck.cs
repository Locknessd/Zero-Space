using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace FrankRetarget.Editor
{
    [InitializeOnLoad]
    public static class FrankTesterPlayCheck
    {
        const string Key="FrankCombinationLiveCheck";
        static int step,frames,lastFrame=-1,observed;
        static float maxContact,maxPelvis,heldTime;
        static double began;
        static Animator mankey,pepe;
        static FrankTesterPlayCheck(){EditorApplication.update+=Tick;}
        public static void Start()
        {
            EditorSceneManager.OpenScene(FrankRetargetBuilder.Output+"/Frank_Damages_Mankey_Pepe.unity",OpenSceneMode.Single);
            var view=EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView"));
            view.Show();view.Focus();view.maximized=true;
            SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;
        }
        static void Tick()
        {
            if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
            try
            {
                Application.runInBackground=true;
                var tester=Object.FindFirstObjectByType<FrankCombinationTester>();
                if(!tester||!tester.Attacker.activeDriver)return;
                if(began==0)began=EditorApplication.timeSinceStartup;
                if(EditorApplication.timeSinceStartup-began>180)throw new Exception("Live test timeout.");
                if(Time.frameCount==lastFrame)return;lastFrame=Time.frameCount;frames++;observed++;
                if(!mankey){mankey=tester.mankey.character;pepe=tester.pepe.character;tester.SelectLibrary(false);tester.pepeAttacks=false;tester.paused=false;tester.Configure();}
                if(mankey!=tester.mankey.character||pepe!=tester.pepe.character)throw new Exception("Characters replaced.");
                if(Object.FindObjectsByType<FrankTestActor>(FindObjectsSortMode.None).Length!=2)throw new Exception("Character count changed.");
                foreach(var actor in new[]{tester.mankey,tester.pepe})
                {
                    var p=actor.Pose;maxPelvis=Mathf.Max(maxPelvis,Vector3.Distance(p.sourceHips.position,p.targetHips.position));
                    foreach(var l in p.limbs)maxContact=Mathf.Max(maxContact,l.alignGrip?Vector3.Distance(l.SourceGrip,l.TargetGrip):Vector3.Distance(l.end.position,l.Goal));
                }
                if(frames<12)return;
                switch(step)
                {
                    case 0:
                        if(tester.time<=0)throw new Exception("Playback did not advance.");
                        tester.paused=true;tester.Seek(.7f);heldTime=tester.time;
                        ScreenCapture.CaptureScreenshot(FrankRetargetBuilder.Output+"/Reports/tester-ui.png");break;
                    case 1:
                        if(tester.time!=heldTime)throw new Exception("Pause changed time.");
                        tester.motion=1;tester.attackerWeapon=7;tester.receiverWeapon=6;tester.Configure();tester.Seek(2);break;
                    case 2:
                        tester.SwapRoles();if(!tester.pepeAttacks)throw new Exception("Role swap failed.");
                        tester.motion=6;tester.attackerWeapon=6;tester.receiverWeapon=5;tester.Configure();tester.Seek(.9f);
                        ScreenCapture.CaptureScreenshot(FrankRetargetBuilder.Output+"/Reports/tester-mixed-ui.png");break;
                    case 3:
                        tester.motion=7;tester.Configure();if(!tester.Receiver.clip.name.EndsWith("Hit2"))throw new Exception("Alternate missing.");
                        tester.Restart();if(tester.time!=0)throw new Exception("Restart failed.");
                        tester.speed=2;tester.loop=true;tester.Seek(tester.Duration-.001f);tester.paused=false;break;
                    case 4:
                        if(tester.time>=tester.Duration)throw new Exception("Loop failed.");
                        tester.loop=false;tester.Seek(tester.Duration-.001f);tester.paused=false;break;
                    case 5:
                        if(!tester.paused||tester.time!=tester.Duration)throw new Exception("Final hold failed.");
                        Finish(maxPelvis<.001f&&maxContact<.025f,null);return;
                }
                step++;frames=0;
            }
            catch(Exception e){Finish(false,e.ToString());}
        }
        static void Finish(bool pass,string error)
        {
            File.WriteAllText(FrankRetargetBuilder.Output+"/Reports/tester-play-mode.txt",$"Unity: {Application.unityVersion}\nPassed: {pass}\nObserved runtime frames: {observed}\nMax pelvis error: {maxPelvis:R}\nMax contact error: {maxContact:R}\nChecked: exactly two persistent models, real playback, pause, seeking, independent equipment, role swap, alternate Warrior, restart, speed, loop, final hold.\n{error}");
            SessionState.SetBool(Key,false);EditorApplication.isPlaying=false;
            EditorApplication.delayCall+=()=>{var view=EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView"));view.maximized=false;};
            step=frames=observed=0;lastFrame=-1;began=0;mankey=pepe=null;maxContact=maxPelvis=0;
        }
    }
    public static partial class FrankRetargetBuilder
    {
        [MenuItem("Tools/Frank Retarget/Run combination tester in Play mode")]
        public static void TesterPlayCheck(){FrankTesterPlayCheck.Start();}
    }
}
