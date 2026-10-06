using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrankRetarget.Editor
{
    [InitializeOnLoad]
    public static class FrankRetargetPlayCheck
    {
        const string Key="FrankRetarget.PlayCheck";
        static double started=-1;
        static float startedGameTime;
        static int phase=-1,frames;
        static float hipError,contactError;
        static float maximumAnimationTime;
        static string failures="";
        static FrankDemoControls demo;
        static FrankRetargetPlayCheck(){EditorApplication.update+=Tick;}
        public static void Start()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Already in Play mode.");
            SessionState.SetString(Key+".previous",SceneManager.GetActiveScene().path);
            var scene=SceneManager.GetSceneByPath(FrankRetargetBuilder.Output+"/Frank_Damages_Mankey_Pepe.unity");
            bool opened=!scene.IsValid()||!scene.isLoaded;
            SessionState.SetBool(Key+".opened",opened);
            if(opened)scene=EditorSceneManager.OpenScene(FrankRetargetBuilder.Output+"/Frank_Damages_Mankey_Pepe.unity",OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            SessionState.SetString(Key,"running");EditorApplication.isPlaying=true;
        }
        static void Tick()
        {
            string status=SessionState.GetString(Key,"");
            if(status=="finishing" && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                var previous=SceneManager.GetSceneByPath(SessionState.GetString(Key+".previous",""));
                if(previous.IsValid())SceneManager.SetActiveScene(previous);
                var scene=SceneManager.GetSceneByPath(FrankRetargetBuilder.Output+"/Frank_Damages_Mankey_Pepe.unity");
                if(scene.IsValid()&&SceneManager.sceneCount>1&&SessionState.GetBool(Key+".opened",false))EditorSceneManager.CloseScene(scene,true);
                SessionState.EraseString(Key);AssetDatabase.Refresh();return;
            }
            if(status!="running" || !EditorApplication.isPlaying)return;
            try
            {
                if(started<0)
                {
                    started=EditorApplication.timeSinceStartup;
                    startedGameTime=Time.time;
                    Application.runInBackground=true;
                    demo=UnityEngine.Object.FindAnyObjectByType<FrankDemoControls>();
                    if(!demo)throw new Exception("Demo controls missing in Play mode.");
                    for(int i=0;i<SceneManager.sceneCount;i++)
                    {
                        var s=SceneManager.GetSceneAt(i);
                        if(s!=demo.gameObject.scene)foreach(var root in s.GetRootGameObjects())root.SetActive(false);
                    }
                }
                // Advance phases using game time: a background editor may clamp deltaTime
                // and play substantially less than one second of animation per wall second.
                double elapsed=Time.time-startedGameTime;
                if(EditorApplication.timeSinceStartup-started>180)throw new Exception("Play-mode test timed out before 25 seconds of game time.");
                int next=elapsed<9?0:elapsed<18?1:elapsed<25?2:3;
                if(next!=phase)
                {
                    phase=next;
                    if(phase==3){Finish();return;}
                    demo.variants[0].SetActive(phase==0);demo.variants[1].SetActive(phase!=0);
                    foreach(var a in demo.variants[phase==0?0:1].GetComponentsInChildren<FrankPoseRetarget>())
                    {
                        a.driver.Rebind();a.driver.Update(0);
                        if(phase==2 && !a.isAttacker && a.weapon=="Warrior")a.driver.Play("Damage_Critical_Warrior_Hit2",0,0);
                        a.ApplyPose();
                    }
                }
                foreach(var a in demo.variants[phase==0?0:1].GetComponentsInChildren<FrankPoseRetarget>())
                {
                    hipError=Mathf.Max(hipError,Vector3.Distance(a.targetHips.position,a.sourceHips.position));
                    maximumAnimationTime=Mathf.Max(maximumAnimationTime,a.driver.GetCurrentAnimatorStateInfo(0).normalizedTime);
                    foreach(var limb in a.limbs)contactError=Mathf.Max(contactError,limb.error);
                    if(a.originalBody.Any(r=>r.enabled))throw new Exception("Original body visible: "+a.name);
                    if(a.weaponRenderers.Any(r=>!r.enabled))throw new Exception("Weapon hidden: "+a.name);
                    if(float.IsNaN(a.targetHips.position.sqrMagnitude))throw new Exception("Invalid pose: "+a.name);
                }
                frames++;
            }
            catch(Exception e){failures+=e+"\n";Finish();}
        }
        static void Finish()
        {
            // EditorApplication.update can be throttled to 1 Hz when the editor is
            // unfocused; animation progress, not editor callback frequency, proves playback.
            bool passed=failures.Length==0 && frames>=20 && phase==3 && maximumAnimationTime>0.5f && hipError<0.001f && contactError<0.005f;
            File.WriteAllText(FrankRetargetBuilder.Output+"/Reports/play-mode.txt",
                "Unity "+Application.unityVersion+"\nPassed: "+passed+"\nObserved editor updates: "+frames+"\nMaximum normalized animation time: "+maximumAnimationTime+"\nMaximum pelvis error (m): "+hipError+"\nMaximum contact error (m): "+contactError+
                "\n25 seconds of simulated Play-mode gameplay: both role variants, restart, and alternate Warrior reaction. All original bodies hidden; weapon renderers retained.\n"+failures);
            SessionState.SetString(Key,"finishing");EditorApplication.isPlaying=false;
        }
    }
    public static partial class FrankRetargetBuilder
    {
        [MenuItem("Tools/Frank Retarget/Legacy/5. Run Play mode verification")]
        public static void PlayCheck()=>FrankRetargetPlayCheck.Start();
    }
}
