using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;
namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void SpacingPlayCheck(){FrankSpacingPlayCheck.Start();}
    }
    [InitializeOnLoad]
    public static class FrankSpacingPlayCheck
    {
        const string Key="FrankSpacingPlayCheck";
        static int step,frames,last=-1;static double started;
        static readonly int[] motions={1,2,10,16,17,1,2,10,16,17};
        static FrankSpacingPlayCheck(){EditorApplication.update+=Tick;}
        public static void Start()
        {
            Directory.CreateDirectory("Assets/DemoSence/Reports/Spacing");
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
                    else if(step==10){
                        t.unarmedMotion=16;t.pepeAttacks=false;t.Configure();t.Seek(t.Duration*.4f);
                        t.bodySpacing=0;t.Evaluate();
                        foreach(var actor in new[]{t.Attacker,t.Receiver})if(Vector3.Distance(actor.Pose.sourceHips.position,actor.Pose.targetHips.position)>.001f)throw new Exception("Zero spacing did not restore authored pelvis");
                        t.bodySpacing=1.25f;t.Evaluate();
                        foreach(var actor in new[]{t.Attacker,t.Receiver})foreach(var limb in actor.Pose.limbs)if(limb.error>.035f)throw new Exception("Spacing slider overextended a limb");
                        t.bodySpacing=1;t.Restart();if(t.time!=0)throw new Exception("Restart");t.speed=2;t.loop=true;t.Seek(t.Duration-.001f);t.paused=false;}
                    else if(step==11){if(t.time>=t.Duration)throw new Exception("Loop");t.loop=false;t.Seek(t.Duration-.001f);t.paused=false;}
                    else if(step==12){if(!t.paused||t.time!=t.Duration)throw new Exception("End hold");t.SelectLibrary(false);t.motion=1;t.attackerWeapon=4;t.Configure();t.Seek(5.29f);}
                    else {Finish(null);return;}
                }
                if(++frames<8)return;
                if(step<10)ScreenCapture.CaptureScreenshot($"Assets/DemoSence/Reports/Spacing/{t.Attacker.characterName}-{t.unarmedLibrary.pairs[t.unarmedMotion].sourceName}.png");
                else if(step==12)ScreenCapture.CaptureScreenshot("Assets/DemoSence/Reports/Spacing/Frank-library.png");
                step++;frames=-4;
            }
            catch(Exception e){Finish(e.ToString());}
        }
        static void Finish(string error)
        {
            File.WriteAllText("Assets/DemoSence/Reports/Spacing/play-mode.txt",$"Passed: {error==null}\nLive captured poses on both characters; spacing slider zero/max, restart, speed, looping, end hold and switching back to Frank.\n{error}");
            SessionState.SetBool(Key,false);EditorApplication.isPlaying=false;
            EditorApplication.delayCall+=()=>EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).maximized=false;
            step=frames=0;last=-1;started=0;
        }
    }
}
