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
    public static class FrankPosturePlayCheck
    {
        const string Key="FrankPostureCheck";
        static int step,frames,lastFrame=-1;
        static float maxError;
        static double start;
        static FrankPosturePlayCheck(){EditorApplication.update+=Tick;}
        public static void Start()
        {
            Directory.CreateDirectory("Assets/DemoSence/Reports/Posture");
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
                var t=Object.FindFirstObjectByType<FrankCombinationTester>();if(!t||!t.Attacker.activeDriver)return;
                if(start==0)start=EditorApplication.timeSinceStartup;
                if(EditorApplication.timeSinceStartup-start>180)throw new Exception("Posture check timeout.");
                if(lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
                if(step>=4){if(++frames>=1)Finish(maxError<.1f,null);return;}
                if(frames==0)
                {
                    t.gunSword=false;t.unarmed=false;t.paused=true;t.pepeAttacks=step<2;t.motion=step%2==0?1:5;t.attackerWeapon=4;t.receiverWeapon=0;t.Configure();t.Seek(step%2==0?5.29f:6.64f);
                }
                if(++frames<5)return;
                foreach(var actor in new[]{t.Attacker,t.Receiver})
                    foreach(var b in actor.Pose.Posture.cervical.Where(b=>b.name=="Head").Concat(actor.Pose.Posture.toes))maxError=Mathf.Max(maxError,Quaternion.Angle(b.Goal,b.target.rotation));
                ScreenCapture.CaptureScreenshot($"Assets/DemoSence/Reports/Posture/{t.Attacker.characterName}-{(step%2==0?"Assassin":"Spear")}.png");
                step++;frames=-3; // allow the requested frame to be written before the next pose
            }
            catch(Exception e){Finish(false,e.ToString());}
        }
        static void Finish(bool pass,string error)
        {
            File.WriteAllText("Assets/DemoSence/Reports/Posture/play-mode.txt",$"Passed: {pass}\nMaximum head/toe rotation error: {maxError:R} degrees\nAssassin 5.29s and Spear 6.64s, both characters in both roles.\n{error}");
            SessionState.SetBool(Key,false);EditorApplication.isPlaying=false;
            EditorApplication.delayCall+=()=>EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).maximized=false;
            step=frames=0;lastFrame=-1;start=0;maxError=0;
        }
    }
    public static partial class FrankRetargetBuilder
    {
        [MenuItem("Tools/Frank Retarget/Verify neck and foot poses in Play mode")]
        public static void PosturePlayCheck(){FrankPosturePlayCheck.Start();}
    }
}
