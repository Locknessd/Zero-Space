using UnityEngine;

namespace FrankRetarget
{
    public sealed class FrankDemoControls : MonoBehaviour
    {
        public GameObject[] variants;
        public Camera demoCamera;
        static readonly string[] Weapons={"2Handed","Assassin","Dual","GreatSword","Katana","Spear","Warrior"};
        static readonly float[] Locations={-15.43f,-9.69f,-2.93f,3.78f,9.82f,15.14f,20.61f};
        int selected=3,variant;
        bool paused;
        void Awake(){FocusPair();}
        void FocusPair()
        {
            demoCamera.transform.position=new Vector3(Locations[selected]+4,3.8f,4);
            demoCamera.transform.LookAt(new Vector3(Locations[selected],1.6f,-1.5f));
        }
        public void Restart()
        {
            foreach(var a in variants[variant].GetComponentsInChildren<FrankPoseRetarget>())
            {
                a.driver.Rebind();a.driver.Update(0);a.driver.speed=paused?0:1;a.ApplyPose();
            }
        }
        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(12,12,660,140),GUI.skin.box);
            GUILayout.Label("Frank Damages • Mankey & Pepe");
            GUILayout.BeginHorizontal();
            if(GUILayout.Button(variant==0?"Mankey attacks / Pepe reacts":"Pepe attacks / Mankey reacts"))
            {
                variants[variant].SetActive(false);variant=1-variant;variants[variant].SetActive(true);Restart();
            }
            if(GUILayout.Button("Restart pairs"))Restart();
            if(GUILayout.Button(paused?"Resume":"Pause"))
            {
                paused=!paused;
                foreach(var a in variants[variant].GetComponentsInChildren<FrankPoseRetarget>())a.driver.speed=paused?0:1;
            }
            GUILayout.EndHorizontal();
            int next=GUILayout.Toolbar(selected,Weapons);
            if(next!=selected){selected=next;FocusPair();}
            if(selected==6 && GUILayout.Button("Play Warrior alternate reaction"))
            {
                Restart();
                foreach(var a in variants[variant].GetComponentsInChildren<FrankPoseRetarget>())
                    if(a.weapon=="Warrior"&&!a.isAttacker)a.driver.Play("Damage_Critical_Warrior_Hit2",0,0);
            }
            GUILayout.Label("All seven original pairs run together. Select a weapon to move the camera.");
            GUILayout.EndArea();
        }
    }
}
