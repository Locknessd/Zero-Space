using UnityEngine;

namespace FrankRetarget
{
    [DefaultExecutionOrder(11000)]
    public sealed class FrankCombinationTester : MonoBehaviour
    {
        public FrankTestActor mankey, pepe;
        public FrankWeaponRig[] weapons;
        public Vector3[] receiverOffsets;
        public Quaternion[] receiverRotations;
        public Camera demoCamera;
        public FrankCinematicCamera cinematicCamera;
        public FrankUnarmedLibrary unarmedLibrary;
        public FrankComboLibrary comboLibrary;
        public FrankGreatSwordLibrary greatSwordLibrary;
        public GameObject greatSwordWeapon;
        public bool gunSword;
        public int comboMotion;
        public FrankPairSpacing pairSpacing;
        [Range(0,1.25f)] public float bodySpacing=1;
        public bool unarmed;
        public int unarmedMotion;
        public bool greatSword;
        public int greatSwordMotion;
        Vector2 selectionScroll;
        public int motion=3, attackerWeapon=4, receiverWeapon;
        public bool pepeAttacks, paused, loop=true;
        public float speed=1, time;
        public static readonly string[] MotionNames={"2-Handed","Assassin","Dual","Greatsword","Katana","Spear","Warrior","Warrior · alt hit"};
        public static readonly string[] WeaponNames={"None","Heavy axe","Assassin sword","Dual swords","Greatsword","Katana","Spear","Sword + shield"};
        public FrankTestActor Attacker => pepeAttacks?pepe:mankey;
        public FrankTestActor Receiver => pepeAttacks?mankey:pepe;
        public float Duration => Attacker.clip&&Receiver.clip?Mathf.Max(Attacker.clip.length,Receiver.clip.length):1;
        GUIStyle title, subtitle, label, button, selected, small;
        Texture2D panel;
        int cameraView;
        float zoom=2.4f;
        void Start(){Configure();}
        public void Configure()
        {
            if(greatSword && greatSwordLibrary && greatSwordLibrary.pairs != null && greatSwordLibrary.pairs.Length > 0)
            {
                greatSwordMotion=Mathf.Clamp(greatSwordMotion,0,greatSwordLibrary.pairs.Length-1);
                var pair=greatSwordLibrary.pairs[greatSwordMotion];
                Attacker.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                Receiver.transform.SetPositionAndRotation(pair.receiverOffset,pair.receiverRotation);
                Attacker.ConfigureGreatSword(pair.attacker,true,greatSwordWeapon);
                Receiver.ConfigureGreatSword(pair.receiver,false,null);
                time=0;Evaluate(0,paused);return;
            }
            if(gunSword && comboLibrary)
            {
                comboMotion=Mathf.Clamp(comboMotion,0,comboLibrary.pairs.Length-1);
                var pair=comboLibrary.pairs[comboMotion];
                Attacker.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                Receiver.transform.SetPositionAndRotation(pair.receiverOffset,Quaternion.Euler(0,180,0));
                Attacker.ConfigureCombo(pair.attack,true);Receiver.ConfigureCombo(pair.reaction,false);
                time=0;Evaluate(0,paused);return;
            }
            if(unarmed && unarmedLibrary && unarmedLibrary.pairs.Length>0)
            {
                unarmedMotion=Mathf.Clamp(unarmedMotion,0,unarmedLibrary.pairs.Length-1);
                var pair=unarmedLibrary.pairs[unarmedMotion];
                Attacker.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                Receiver.transform.SetPositionAndRotation(pair.receiverOffset,pair.receiverRotation);
                Attacker.ConfigureUnarmed(pair.attacker,true);
                Receiver.ConfigureUnarmed(pair.receiver,false);
                time=0;Evaluate(0,paused);return;
            }
            int index=Mathf.Min(motion,6);
            Attacker.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            Receiver.transform.SetPositionAndRotation(receiverOffsets[index],receiverRotations[index]);
            Attacker.Configure(index,true,false,attackerWeapon==0?null:weapons[attackerWeapon-1],attackerWeapon-1);
            Receiver.Configure(index,false,motion==7,receiverWeapon==0?null:weapons[receiverWeapon-1],receiverWeapon-1);
            time=0;Evaluate(0,paused);
        }
        public void SelectLibrary(bool bareHands){gunSword=false;greatSword=false;unarmed=bareHands;selectionScroll=Vector2.zero;Configure();}
        public void SelectFrankWeapons(){SelectLibrary(false);}
        public void SelectCombos(){gunSword=true;greatSword=false;unarmed=false;selectionScroll=Vector2.zero;Configure();}
        public void SelectGreatSword(){gunSword=false;unarmed=false;greatSword=true;selectionScroll=Vector2.zero;Configure();}
        public void SwapRoles(){pepeAttacks=!pepeAttacks;Configure();}
        public void Restart(){time=0;Evaluate(0,true);}
        public void Seek(float seconds){time=Mathf.Clamp(seconds,0,Duration);Evaluate(0,true);}
        public void Evaluate(float cameraDeltaTime=0,bool immediateCamera=false)
        {
            Attacker.Evaluate(time);Receiver.Evaluate(time);
            if(gunSword && comboLibrary)
                Receiver.Pose.targetHips.position+=Vector3.up*comboLibrary.pairs[comboMotion].FloorOffset(Receiver==mankey,time);
            if(!gunSword && unarmed && pairSpacing && bodySpacing>0)
                FrankPairSpacing.Apply(Attacker,Receiver,pairSpacing.Separation(unarmedMotion,pepeAttacks,time)*bodySpacing);
            FrameCamera(cameraDeltaTime,immediateCamera);
        }
        void LateUpdate()
        {
            if(!paused)
            {
                time+=Time.deltaTime*speed;
                if(time>Duration) {if(loop)time%=Duration;else {time=Duration;paused=true;}}
            }
            Evaluate(paused?0:Time.deltaTime);
        }
        void FrameCamera(float deltaTime=0,bool immediate=false)
        {
            if(!demoCamera)return;
            float scale=Mathf.Min(Screen.height/820f,Screen.width/1100f);
            float left=370*scale/Mathf.Max(1,Screen.width);
            demoCamera.rect=new Rect(left,0,1-left,1);
            demoCamera.aspect=Mathf.Max(.05f,(float)demoCamera.pixelWidth/Mathf.Max(1,demoCamera.pixelHeight));
            if(!cinematicCamera)
            {
                cinematicCamera=GetComponent<FrankCinematicCamera>();
                if(!cinematicCamera)cinematicCamera=demoCamera.GetComponent<FrankCinematicCamera>();
            }
            if(cinematicCamera)
            {
                cinematicCamera.tester=this;
                cinematicCamera.Zoom=zoom/2.4f;
                if(cinematicCamera.Apply(deltaTime,immediate))return;
            }
            Vector3 focus=new Vector3(0,1.2f,receiverOffsets[Mathf.Min(motion,6)].z*0.5f);
            float separation=0;
            if(Attacker.Pose && Receiver.Pose)
            {
                var a=Attacker.Pose.targetHips.position;var b=Receiver.Pose.targetHips.position;
                focus=(a+b)*.5f+Vector3.up*.3f;focus.y=Mathf.Max(.9f,focus.y);
                separation=Vector3.Distance(a,b);
            }
            Vector3[] views={new Vector3(6,3,6),new Vector3(8,2,0),new Vector3(0,2,8)};
            demoCamera.transform.position=focus+views[cameraView];demoCamera.transform.LookAt(focus);
            demoCamera.orthographic=true;demoCamera.orthographicSize=Mathf.Max(zoom,separation*.6f+1.1f);
        }
        void Styles()
        {
            if(title!=null)return;
            panel=new Texture2D(1,1);panel.SetPixel(0,0,new Color(.065f,.08f,.11f,.98f));panel.Apply();
            title=new GUIStyle(GUI.skin.label){fontSize=25,fontStyle=FontStyle.Bold,normal={textColor=Color.white}};
            subtitle=new GUIStyle(GUI.skin.label){fontSize=13,normal={textColor=new Color(.55f,.72f,.8f)}};
            label=new GUIStyle(GUI.skin.label){fontSize=14,fontStyle=FontStyle.Bold,normal={textColor=Color.white}};
            small=new GUIStyle(GUI.skin.label){fontSize=12,wordWrap=true,normal={textColor=new Color(.7f,.75f,.8f)}};
            button=new GUIStyle(GUI.skin.button){fontSize=13,fixedHeight=29,margin=new RectOffset(3,3,3,3)};
            selected=new GUIStyle(button){fontStyle=FontStyle.Bold,normal={textColor=new Color(.4f,1,.8f)}};
        }
        int Grid(int current,string[] names)
        {
            for(int row=0;row<(names.Length+1)/2;row++)
            {
                GUILayout.BeginHorizontal();
                for(int col=0;col<2;col++)
                {
                    int i=row*2+col;if(i>=names.Length){GUILayout.FlexibleSpace();continue;}
                    if(GUILayout.Button((current==i?"• ":"")+names[i],current==i?selected:button,GUILayout.Width(148)))current=i;
                }
                GUILayout.EndHorizontal();
            }
            return current;
        }
        void OnGUI()
        {
            Styles();
            float scale=Mathf.Min(Screen.height/820f,Screen.width/1100f);
            var old=GUI.matrix;GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
            GUI.DrawTexture(new Rect(0,0,370,Screen.height/scale),panel);
            GUILayout.BeginArea(new Rect(18,14,334,798));
            GUILayout.Label("Motion × Weapon",title);
            GUILayout.Label("MANKEY + PEPE  /  COMBINATION LAB",subtitle);
            GUILayout.Space(10);
            GUILayout.Label(Attacker.characterName+" hits "+Receiver.characterName,label);
            if(GUILayout.Button("⇄  Swap character roles",button))SwapRoles();
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Frank weapons",!unarmed&&!gunSword&&!greatSword?selected:button))SelectFrankWeapons();
            if(unarmedLibrary && GUILayout.Button("Vol10 · bare hands",unarmed&&!gunSword?selected:button))SelectLibrary(true);
            GUILayout.EndHorizontal();
            if(comboLibrary && GUILayout.Button("Gun + sword · combos",gunSword?selected:button))SelectCombos();
            if(greatSwordLibrary && GUILayout.Button("GreatSword · executions",greatSword?selected:button))SelectGreatSword();
            selectionScroll=GUILayout.BeginScrollView(selectionScroll,GUILayout.Height(400));
            if(greatSword && greatSwordLibrary)
            {
                GUILayout.Label("EXECUTION SAMPLE",label);
                var names=System.Array.ConvertAll(greatSwordLibrary.pairs,p=>p.label);
                int next=Grid(greatSwordMotion,names);
                if(next!=greatSwordMotion){greatSwordMotion=next;Configure();}
                GUILayout.Space(8);
                GUILayout.Label("Original GreatSword execution takes, retargeted to Mankey and Pepe. The sword follows the authored right-hand grip.",small);
            }
            else if(gunSword && comboLibrary)
            {
                for(int group=1;group<=3;group++)
                {
                    GUILayout.Label("COMBO "+group.ToString("00"),label);
                    GUILayout.BeginHorizontal();int column=0;
                    for(int i=0;i<comboLibrary.pairs.Length;i++)
                    {
                        var pair=comboLibrary.pairs[i];if(pair.combo!=group)continue;
                        if(column==2){GUILayout.EndHorizontal();GUILayout.BeginHorizontal();column=0;}
                        if(GUILayout.Button((i==comboMotion?"• ":"")+pair.label,i==comboMotion?selected:button,GUILayout.Width(148)))
                        {comboMotion=i;Configure();}
                        column++;
                    }
                    GUILayout.EndHorizontal();GUILayout.Space(5);
                }
                GUILayout.Label("Original gun + sword. Each step has its own hit reaction and ground finish.",small);
            }
            else if(unarmed && unarmedLibrary)
            {
                GUILayout.Label("01  UNARMED ANIMATION PAIR",label);
                var names=System.Array.ConvertAll(unarmedLibrary.pairs,p=>p.label);
                int next=Grid(unarmedMotion,names);
                if(next!=unarmedMotion){unarmedMotion=next;Configure();}
                GUILayout.Space(8);
                GUILayout.Label("Bare hands on both characters. Each button plays the original active / passive pair.",small);
                if(pairSpacing)
                {
                    GUILayout.Space(10);
                    GUILayout.Label($"Body spacing  {bodySpacing*100:0}%",small);
                    float spacing=GUILayout.HorizontalSlider(bodySpacing,0,1.25f);
                    if(Mathf.Abs(spacing-bodySpacing)>.0001f){bodySpacing=spacing;Evaluate();}
                }
            }
            else
            {
            GUILayout.Space(8);GUILayout.Label("01  ANIMATION PAIR",label);
            int nextMotion=Grid(motion,MotionNames);
            GUILayout.Space(7);GUILayout.Label("02  ATTACKER WEAPON",label);
            int nextWeapon=Grid(attackerWeapon,WeaponNames);
            GUILayout.Space(7);GUILayout.Label("03  RECEIVER WEAPON",label);
            int nextReceiver=Grid(receiverWeapon,WeaponNames);
            if(nextMotion!=motion||nextWeapon!=attackerWeapon||nextReceiver!=receiverWeapon)
            {motion=nextMotion;attackerWeapon=nextWeapon;receiverWeapon=nextReceiver;Configure();}
            }
            GUILayout.EndScrollView();
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if(GUILayout.Button(paused?"▶  Play":"Ⅱ  Pause",button))paused=!paused;
            if(GUILayout.Button("↻  Restart",button))Restart();
            loop=GUILayout.Toggle(loop,"Loop",GUILayout.Width(62));GUILayout.EndHorizontal();
            GUILayout.Label($"{time:0.00}s / {Duration:0.00}s",small);
            float scrub=GUILayout.HorizontalSlider(time,0,Duration);
            if(Mathf.Abs(scrub-time)>.0001f){paused=true;Seek(scrub);}
            GUILayout.BeginHorizontal();GUILayout.Label($"Speed  {speed:0.00}×",small,GUILayout.Width(95));
            speed=GUILayout.HorizontalSlider(speed,.1f,2f);GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            bool cinematic=cinematicCamera&&cinematicCamera.cinematic&&cinematicCamera.library;
            if(GUILayout.Button("Camera: "+(cinematic?"Cinematic":new[]{"3/4","Side","Front"}[cameraView]),button))
            {
                if(cinematic){cinematicCamera.cinematic=false;cameraView=0;}
                else if(cameraView==2&&cinematicCamera&&cinematicCamera.library)
                {cinematicCamera.cinematic=true;cinematicCamera.ResetView();}
                else cameraView=(cameraView+1)%3;
                FrameCamera(0,true);
            }
            if(GUILayout.Button("−",button,GUILayout.Width(34))){zoom=Mathf.Min(7,zoom+.4f);FrameCamera(0,true);}
            if(GUILayout.Button("+",button,GUILayout.Width(34))){zoom=Mathf.Max(1.5f,zoom-.4f);FrameCamera(0,true);}
            GUILayout.EndHorizontal();
            GUILayout.Label(greatSword?"GreatSword_Animset Execution_Sample: four matched attack / reaction pairs.":gunSword?"Full combo or individual step. The shorter clip holds while the receiver finishes falling.":unarmed?"Vol10: 17 paired actions + idle. Swap roles to try either character. Scrub to inspect a pose.":"Choose motion and weapons independently. Each change restarts both actors.",small);
            GUILayout.EndArea();GUI.matrix=old;
        }
        void OnDestroy(){if(panel)Destroy(panel);}
    }
}
