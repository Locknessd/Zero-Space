using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        const string CameraFolder = Output + "/Cinematography";
        const string CameraLibraryPath = CameraFolder + "/CinematicCameraLibrary.asset";
        [Serializable] public class CameraPlans { public CameraPlan[] plans; }
        [Serializable] public class CameraPlan
        {
            public string family, direction;
            public int index;
            public float[] beats, yaw, elevation, fov, margin;
            public float[] pepeYaw; // Optional role-specific angle for asymmetric silhouettes.
            public float tracking, lead;
        }
        sealed class CameraPoseSample
        {
            public float time;
            public Vector3 center;
            public Vector3[] points;
        }

        // Each plan is authored independently in seconds. Smooth Hermite tangents retain
        // continuous movement through intermediate beats and ease at the ends of a take.
        static float CameraCurve(float[] times, float[] values, float t)
        {
            if (t <= times[0]) return values[0];
            int last = times.Length - 1;
            if (t >= times[last]) return values[last];
            int i = 0;
            while (i + 1 < last && times[i + 1] < t) i++;
            float duration = times[i + 1] - times[i], u = (t - times[i]) / duration;
            float m0 = i == 0 ? 0 : CameraTangent(times, values, i);
            float m1 = i + 1 == last ? 0 : CameraTangent(times, values, i + 1);
            return (2*u*u*u-3*u*u+1)*values[i] + (u*u*u-2*u*u+u)*duration*m0 +
                   (-2*u*u*u+3*u*u)*values[i+1] + (u*u*u-u*u)*duration*m1;
        }
        static float CameraTangent(float[] times, float[] values, int i)
        {
            float a = (values[i]-values[i-1])/(times[i]-times[i-1]);
            float b = (values[i+1]-values[i])/(times[i+1]-times[i]);
            return a*b <= 0 ? 0 : 2*a*b/(a+b);
        }
        static void CheckCameraPlan(CameraPlan p, float duration)
        {
            if (p.beats == null || p.beats.Length < 5 || p.yaw.Length != p.beats.Length ||
                p.elevation.Length != p.beats.Length || p.fov.Length != p.beats.Length || p.margin.Length != p.beats.Length)
                throw new Exception("Invalid camera direction arrays " + p.family + "/" + p.index);
            if (p.pepeYaw != null && p.pepeYaw.Length != p.beats.Length) throw new Exception("Invalid Pepe camera angle curve");
            for (int i=1;i<p.beats.Length;i++) if(p.beats[i]<=p.beats[i-1]) throw new Exception("Unordered camera beats");
            if (Mathf.Abs(p.beats[0]) > .001f || Mathf.Abs(p.beats[p.beats.Length-1]-duration) > .03f)
                throw new Exception("Camera plan duration differs from animation " + p.family + "/" + p.index);
        }
        static float[] SmoothCameraValues(float[] values,float step,float sigma)
        {
            var result = new float[values.Length];int radius=Mathf.CeilToInt(sigma*3/step);
            for(int i=0;i<values.Length;i++)
            {
                float total=0,weight=0;
                for(int k=-radius;k<=radius;k++)
                {float w=Mathf.Exp(-.5f*k*k*step*step/(sigma*sigma));total+=values[Mathf.Clamp(i+k,0,values.Length-1)]*w;weight+=w;}
                result[i]=total/weight;
            }
            return result;
        }
        static FrankCameraLibrary.Shot BakeCameraTake(FrankCombinationTester tester,FrankCinematicCamera director,CameraPlan plan,string label,int role)
        {
            SelectCameraEntry(tester,plan.family,plan.index,role);
            CheckCameraPlan(plan,tester.Duration);
            int count=Mathf.CeilToInt(tester.Duration*30)+1;
            float step=tester.Duration/(count-1);
            var samples=new CameraPoseSample[count];var points=new List<Vector3>();
            for(int i=0;i<count;i++)
            {
                float t=step*i;tester.Seek(t);director.CollectFramingPoints(points);
                if(points.Count==0)throw new Exception("No visible geometry for camera");
                Bounds bounds=new Bounds(points[0],Vector3.zero);foreach(var v in points)bounds.Encapsulate(v);
                // The visible action envelope controls composition; a small body-center bias
                // avoids aim jitter when a long weapon changes hands or crosses the pair.
                Vector3 bodies=(tester.Attacker.Pose.targetHips.position+tester.Receiver.Pose.targetHips.position)*.5f+Vector3.up*.15f;
                samples[i]=new CameraPoseSample{time=t,center=Vector3.Lerp(bounds.center,bodies,.15f),points=points.ToArray()};
            }
            var frames=new FrankCameraLibrary.Frame[count];var fit=new float[count];
            float sigma=Mathf.Clamp(plan.tracking,.10f,.5f), lead=Mathf.Clamp(plan.lead,0,.2f);
            for(int i=0;i<count;i++)
            {
                float t=samples[i].time;Vector3 focus=Vector3.zero;float weight=0;
                // Offline symmetric smoothing anticipates jumps without chase-camera lag.
                int start=Mathf.Max(0,Mathf.FloorToInt((t+lead-sigma*3)/step));
                int end=Mathf.Min(count-1,Mathf.CeilToInt((t+lead+sigma*3)/step));
                for(int j=start;j<=end;j++)
                {float d=(samples[j].time-t-lead)/sigma,w=Mathf.Exp(-.5f*d*d);focus+=samples[j].center*w;weight+=w;}
                focus/=weight;focus.y+=.035f;
                float yaw=CameraCurve(plan.beats,role==1 && plan.pepeYaw!=null ? plan.pepeYaw : plan.yaw,t)*Mathf.Deg2Rad;
                float elevation=CameraCurve(plan.beats,plan.elevation,t)*Mathf.Deg2Rad;
                Vector3 offset=new Vector3(Mathf.Sin(yaw)*Mathf.Cos(elevation),Mathf.Sin(elevation),Mathf.Cos(yaw)*Mathf.Cos(elevation));
                float fov=CameraCurve(plan.beats,plan.fov,t);
                Quaternion inverse=Quaternion.Inverse(Quaternion.LookRotation(-offset,Vector3.up));
                float tanV=Mathf.Tan(fov*Mathf.Deg2Rad*.5f)*.84f;
                float tanH=tanV*1.30f; // Showcase content viewport after the left controls.
                float distance=1;
                foreach(var point in samples[i].points)
                {
                    Vector3 local=inverse*(point-focus);
                    distance=Mathf.Max(distance,Mathf.Max(Mathf.Abs(local.x)/tanH-local.z,Mathf.Abs(local.y)/tanV-local.z));
                }
                fit[i]=distance*CameraCurve(plan.beats,plan.margin,t);
                frames[i]=new FrankCameraLibrary.Frame{time=t,focus=focus,position=offset,fov=fov};
            }
            // Open the shot before an arc or launch, then settle rather than breathing with
            // individual limbs. The final runtime guard covers arbitrary replacement weapons.
            var anticipated=new float[count];int window=Mathf.CeilToInt(.20f/step);
            for(int i=0;i<count;i++)
            {
                anticipated[i]=fit[i];
                for(int j=Mathf.Max(0,i-window);j<=Mathf.Min(count-1,i+window);j++)anticipated[i]=Mathf.Max(anticipated[i],fit[j]);
            }
            // Bound proportional dolly speed before smoothing. This opens early for
            // big arcs and avoids a rushed push back in after an airborne separation.
            float opening=Mathf.Exp(.65f*step),closing=Mathf.Exp(.50f*step);
            for(int i=count-2;i>=0;i--)anticipated[i]=Mathf.Max(anticipated[i],anticipated[i+1]/opening);
            for(int i=1;i<count;i++)anticipated[i]=Mathf.Max(anticipated[i],anticipated[i-1]/closing);
            var radius=SmoothCameraValues(anticipated,step,Mathf.Clamp(plan.tracking,.18f,.38f));
            for(int i=0;i<count;i++)frames[i].position=frames[i].focus+frames[i].position*radius[i];
            return new FrankCameraLibrary.Shot{key=plan.family+"/"+plan.index+"/"+(role==0?"mankey":"pepe"),label=label,direction=plan.direction,duration=tester.Duration,frames=frames};
        }

        public static void CameraBuildAndValidate() { CameraBuild(); CameraValidate(); CameraCaptureBeats(); }

        [MenuItem("Tools/Frank Retarget/Cameras/Bake all directed camera takes")]
        public static void CameraBuild()
        {
            var directions=JsonUtility.FromJson<CameraPlans>(File.ReadAllText(Output+"/Editor/FrankCameraDirections.json"));
            var scene=EditorSceneManager.OpenPreviewScene(Output+"/Frank_Damages_Mankey_Pepe.unity");
            var tester=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<FrankCombinationTester>()).First();
            var director=tester.GetComponent<FrankCinematicCamera>();
            if(!director)director=tester.gameObject.AddComponent<FrankCinematicCamera>();
            director.tester=tester;director.cinematic=false;tester.cinematicCamera=director;
            try
            {
                var entries=CameraEntries(tester).ToArray();
                if(directions.plans.Length!=entries.Length)throw new Exception("Every animation requires exactly one plan");
                var shots=new List<FrankCameraLibrary.Shot>();
                foreach(var entry in entries)
                {
                    var plan=directions.plans.Single(p=>p.family==entry.family&&p.index==entry.index);
                    for(int role=0;role<2;role++)shots.Add(BakeCameraTake(tester,director,plan,entry.label,role));
                    Debug.Log("Cinematic take baked: "+entry.family+"/"+entry.index+" "+entry.label);
                }
                if(!AssetDatabase.IsValidFolder(CameraFolder))AssetDatabase.CreateFolder(Output,"Cinematography");
                var library=AssetDatabase.LoadAssetAtPath<FrankCameraLibrary>(CameraLibraryPath);
                if(!library){library=ScriptableObject.CreateInstance<FrankCameraLibrary>();AssetDatabase.CreateAsset(library,CameraLibraryPath);}
                library.shots=shots.ToArray();EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();
                File.WriteAllText("/tmp/frank-camera-library.json",JsonUtility.ToJson(library));
                Debug.Log("Saved "+shots.Count+" individually directed character camera takes");
            }
            finally{tester.mankey.Clear();tester.pepe.Clear();EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
