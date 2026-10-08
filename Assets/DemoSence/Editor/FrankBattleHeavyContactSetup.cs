using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        const string HeavyContactReview = "GeneratedAssets/BattleHeavyContactReview";
        public static void OpenBattleAfterContactReview()
        {
            var battle=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(SfxScene);
            if(!battle.IsValid() || !battle.isLoaded)battle=EditorSceneManager.OpenScene(SfxScene,OpenSceneMode.Additive);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(battle);
        }
        static readonly HumanBodyBones[] ContactLimbs = { HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot };

        sealed class ContactBody : IDisposable
        {
            readonly SkinnedMeshRenderer[] skins;
            readonly Mesh scratch = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            readonly List<Vector3> local = new List<Vector3>();
            readonly List<Vector3> points = new List<Vector3>();
            readonly List<Vector3> surfaceVertices = new List<Vector3>();
            readonly List<int> surfaceTriangles = new List<int>();
            Vector3[] tree;
            static readonly IComparer<Vector3>[] axes = {
                Comparer<Vector3>.Create((a,b)=>a.x.CompareTo(b.x)),
                Comparer<Vector3>.Create((a,b)=>a.y.CompareTo(b.y)),
                Comparer<Vector3>.Create((a,b)=>a.z.CompareTo(b.z)) };
            public ContactBody(CharacterCombat target)
            {
                skins = target.Animator.GetComponentsInChildren<SkinnedMeshRenderer>()
                    .Where(s=>s.enabled && s.sharedMesh && s.sharedMesh.vertexCount > 1000).ToArray();
                if (skins.Length == 0) throw new Exception("Receiver has no body mesh: " + target.name);
            }
            public void Sample()
            {
                points.Clear();
                surfaceVertices.Clear();surfaceTriangles.Clear();
                foreach(var skin in skins)
                {
                    scratch.Clear();skin.BakeMesh(scratch,true); scratch.GetVertices(local);
                    int offset=surfaceVertices.Count;
                    foreach(var vertex in local)surfaceVertices.Add(skin.transform.TransformPoint(vertex));
                    var triangles=scratch.triangles;
                    foreach(int index in triangles)surfaceTriangles.Add(offset+index);
                    var used=triangles.Distinct().ToArray();int stride=Mathf.Max(1,used.Length/2400);
                    for(int i=0;i<used.Length;i+=stride)points.Add(surfaceVertices[offset+used[i]]);
                }
                tree = points.ToArray(); Build(0,tree.Length,0);
            }
            void Build(int start,int count,int depth)
            {
                if(count<=1)return;
                Array.Sort(tree,start,count,axes[depth%3]);
                int half=count/2; Build(start,half,depth+1); Build(start+half+1,count-half-1,depth+1);
            }
            public float Distance(Vector3 point,out Vector3 nearest)
            {
                float best=float.PositiveInfinity; nearest=point; Search(0,tree.Length,0,point,ref best,ref nearest);
                return Mathf.Sqrt(best);
            }
            void Search(int start,int count,int depth,Vector3 point,ref float best,ref Vector3 nearest)
            {
                if(count==0)return;
                int half=count/2,index=start+half,axis=depth%3;
                var candidate=tree[index]; float distance=(candidate-point).sqrMagnitude;
                if(distance<best){best=distance;nearest=candidate;}
                float delta=point[axis]-candidate[axis];
                if(delta<0){Search(start,half,depth+1,point,ref best,ref nearest); if(delta*delta<best)Search(index+1,count-half-1,depth+1,point,ref best,ref nearest);}
                else{Search(index+1,count-half-1,depth+1,point,ref best,ref nearest); if(delta*delta<best)Search(start,half,depth+1,point,ref best,ref nearest);}
            }
            public Vector3 Surface(Vector3 point)
            {
                float nearestDistance=Distance(point,out var nearest);float best=nearestDistance*nearestDistance;
                for(int i=0;i<surfaceTriangles.Count;i+=3)
                {
                    var a=surfaceVertices[surfaceTriangles[i]];var b=surfaceVertices[surfaceTriangles[i+1]];var c=surfaceVertices[surfaceTriangles[i+2]];
                    var candidate=TrianglePoint(point,a,b,c);float d=(candidate-point).sqrMagnitude;
                    if(d<best){best=d;nearest=candidate;}
                }
                return nearest;
            }
            public bool ClosestToTriangle(Vector3 a,Vector3 b,Vector3 c,float limit,out Vector3 point,out float distance)
            {
                Vector3 min=Vector3.Min(a,Vector3.Min(b,c))-Vector3.one*limit;
                Vector3 max=Vector3.Max(a,Vector3.Max(b,c))+Vector3.one*limit;
                float best=limit*limit;point=a;bool found=false;
                TriangleSearch(0,tree.Length,0,min,max,a,b,c,ref best,ref point,ref found);
                // Do not infer success by recomputing a squared float limit:
                // Mono may retain extra intermediate precision and report a hit
                // without ever assigning a contact point.
                distance=Mathf.Sqrt(best);return found;
            }
            void TriangleSearch(int start,int count,int depth,Vector3 min,Vector3 max,Vector3 a,Vector3 b,Vector3 c,ref float best,ref Vector3 point,ref bool found)
            {
                if(count==0)return;
                int half=count/2,index=start+half,axis=depth%3;var p=tree[index];
                if(p.x>=min.x && p.x<=max.x && p.y>=min.y && p.y<=max.y && p.z>=min.z && p.z<=max.z)
                {
                    var q=TrianglePoint(p,a,b,c);float d=(p-q).sqrMagnitude;
                    if(d<best){best=d;point=q;found=true;}
                }
                if(min[axis]<=p[axis])TriangleSearch(start,half,depth+1,min,max,a,b,c,ref best,ref point,ref found);
                if(max[axis]>=p[axis])TriangleSearch(index+1,count-half-1,depth+1,min,max,a,b,c,ref best,ref point,ref found);
            }
            public void Dispose(){Object.DestroyImmediate(scratch);}
        }

        sealed class ContactProbe : IDisposable
        {
            readonly Mesh scratch = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            readonly List<Vector3> vertices = new List<Vector3>();
            public readonly string[] names = { "Weapon", "Shield", "LeftHand", "RightHand", "LeftFoot", "RightFoot" };
            public readonly float[] distances = new float[6];
            public readonly Vector3[] points = new Vector3[6];
            public void Sample(FrankBattlePairPlayback pair,CharacterCombat attacker,ContactBody body,bool surfaces=false,Func<Renderer,bool> weaponFilter=null)
            {
                for(int i=0;i<distances.Length;i++)distances[i]=float.PositiveInfinity;
                var renderers=pair.AttackerActor.Pose.weaponRenderers;
                foreach(var renderer in renderers)
                {
                    if(!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                        renderer.name.IndexOf("case",StringComparison.OrdinalIgnoreCase)>=0)continue;
                    if(weaponFilter!=null && !weaponFilter(renderer))continue;
                    int kind=renderer.name.IndexOf("shield",StringComparison.OrdinalIgnoreCase)>=0?1:0;
                    Mesh mesh;
                    if(renderer is SkinnedMeshRenderer skin){scratch.Clear();skin.BakeMesh(scratch,true);mesh=scratch;}
                    else{var filter=renderer.GetComponent<MeshFilter>();mesh=filter?filter.sharedMesh:null;}
                    if(!mesh)continue;
                    mesh.GetVertices(vertices);
                    // Use the entire blade, not renderer bounds or the grip.
                    int stride=Mathf.Max(1,vertices.Count/1500);
                    for(int i=0;i<vertices.Count;i+=stride)Consider(kind,renderer.transform.TransformPoint(vertices[i]),body);
                    if(surfaces)
                    {
                        var triangles=mesh.triangles;
                        for(int i=0;i<triangles.Length;i+=3)
                        {
                            var a=renderer.transform.TransformPoint(vertices[triangles[i]]);
                            var b=renderer.transform.TransformPoint(vertices[triangles[i+1]]);
                            var c=renderer.transform.TransformPoint(vertices[triangles[i+2]]);
                            if(body.ClosestToTriangle(a,b,c,Mathf.Min(.08f,distances[kind]),out var point,out float distance))
                            {distances[kind]=distance;points[kind]=point;}
                        }
                    }
                }
                for(int i=0;i<ContactLimbs.Length;i++)
                {
                    var limb=attacker.Animator.GetBoneTransform(ContactLimbs[i]);
                    if(limb)Consider(i+2,limb.position,body);
                    if(i<2)
                    {
                        var finger=attacker.Animator.GetBoneTransform(i==0?HumanBodyBones.LeftMiddleProximal:HumanBodyBones.RightMiddleProximal);
                        if(finger)Consider(i+2,finger.position,body);
                    }
                    else
                    {
                        var toe=attacker.Animator.GetBoneTransform(i==2?HumanBodyBones.LeftToes:HumanBodyBones.RightToes);
                        if(toe)Consider(i+2,toe.position,body);
                    }
                }
            }
            void Consider(int kind,Vector3 point,ContactBody body)
            {
                float distance=body.Distance(point,out _);
                if(distance<distances[kind]){distances[kind]=distance;points[kind]=point;}
            }
            public void Dispose(){Object.DestroyImmediate(scratch);}
        }

        static Vector3 TrianglePoint(Vector3 p,Vector3 a,Vector3 b,Vector3 c)
        {
            Vector3 ab=b-a,ac=c-a,ap=p-a; float d1=Vector3.Dot(ab,ap),d2=Vector3.Dot(ac,ap);
            if(d1<=0 && d2<=0)return a;
            Vector3 bp=p-b; float d3=Vector3.Dot(ab,bp),d4=Vector3.Dot(ac,bp);
            if(d3>=0 && d4<=d3)return b;
            float vc=d1*d4-d3*d2;
            if(vc<=0 && d1>=0 && d3<=0)return a+ab*(d1/(d1-d3));
            Vector3 cp=p-c; float d5=Vector3.Dot(ab,cp),d6=Vector3.Dot(ac,cp);
            if(d6>=0 && d5<=d6)return c;
            float vb=d5*d2-d1*d6;
            if(vb<=0 && d2>=0 && d6<=0)return a+ac*(d2/(d2-d6));
            float va=d3*d6-d5*d4;
            if(va<=0 && d4-d3>=0 && d5-d6>=0)return b+(c-b)*((d4-d3)/(d4-d3+d5-d6));
            float sum=va+vb+vc;
            if(Mathf.Abs(sum)<1e-12f)return a;
            return a+ab*(vb/sum)+ac*(vc/sum);
        }

        public static void SurveyBattleHeavyContacts()
        {
            Directory.CreateDirectory(HeavyContactReview);
            var scene=EditorSceneManager.OpenPreviewScene(SfxScene);
            var csv=new StringBuilder("fighter,move,hit,time,kind,distance,sourceX,sourceY,sourceZ\n");
            var report=new StringBuilder();
            try
            {
                var game=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters=new[]{game.leftCombat,game.rightCombat};
                foreach(var f in fighters){f.battleVfx=null;f.battleSfx=null;f.hitEffect=null;}
                foreach(var source in fighters)
                foreach(var move in source.heavyCombatMoves)
                {
                    var target=fighters.Single(f=>f!=source);
                    foreach(var f in fighters)f.ResetCombat();
                    source.Animator.transform.position=Vector3.zero;
                    target.Animator.transform.position=Vector3.right*move.attackRange;
                    if(!source.ExecuteAttack(move,target))throw new Exception("Cannot survey "+move.moveName);
                    var pair=source.SourcePlayback;
                    var hits=game.battleVfx.timeline.FindMove(move).cues.Where(IsContactCue).ToArray();
                    using(var body=new ContactBody(target))
                    using(var probe=new ContactProbe())
                    for(int hit=0;hit<hits.Length;hit++)
                    {
                        float old=hits[hit].seconds;
                        float from=Mathf.Max(old-.45f,hit==0?0:(old+hits[hit-1].seconds)*.5f);
                        float to=Mathf.Min(old+.35f,hit+1==hits.Length?pair.Duration:(old+hits[hit+1].seconds)*.5f);
                        var best=new float[6];var times=new float[6];
                        for(int k=0;k<6;k++)best[k]=float.PositiveInfinity;
                        for(int frame=0;frame<=Mathf.CeilToInt((to-from)*120);frame++)
                        {
                            float time=Mathf.Min(to,from+frame/120f); pair.EvaluateAt(time); body.Sample(); probe.Sample(pair,source,body);
                            for(int k=0;k<6;k++)
                            {
                                var point=probe.points[k];var d=probe.distances[k];
                                csv.AppendLine(FormattableString.Invariant($"{source.name},{move.moveName},{hit},{time:F5},{probe.names[k]},{d:F5},{point.x:F5},{point.y:F5},{point.z:F5}"));
                                if(d<best[k]){best[k]=d;times[k]=time;}
                            }
                        }
                        report.AppendLine(source.name+"/"+move.moveName+" #"+hit+" old="+old.ToString("F3")+"; "+
                            string.Join("; ",Enumerable.Range(0,6).Select(k=>probe.names[k]+"="+times[k].ToString("F4")+"s/"+best[k].ToString("F4")+"m")));
                        File.WriteAllText(HeavyContactReview+"/ContactSurvey.txt",report.ToString());
                    }
                    pair.Cancel();
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                File.WriteAllText(HeavyContactReview+"/ContactSurvey.csv",csv.ToString());
                File.WriteAllText(HeavyContactReview+"/ContactSurvey.txt",report.ToString());
            }
        }
        static bool IsContactCue(BattleSfxBank.Cue cue)=>cue.group=="light_hit"||cue.group=="heavy_hit"||cue.group=="stab_hit";

        public static void CaptureBattleHeavyContactCandidates()
        {
            var samples=File.ReadAllLines(HeavyContactReview+"/ContactSurvey.csv").Skip(1).Select(line=>line.Split(',')).ToArray();
            var scene=EditorSceneManager.OpenPreviewScene(SfxScene);
            try
            {
                var game=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<GameManager>(true)).Single();
                foreach(var canvas in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Canvas>(true)))canvas.gameObject.SetActive(false);
                var camera=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Camera>(true)).Single(c=>c.CompareTag("MainCamera"));
                camera.scene=scene;camera.orthographic=true;camera.enabled=false;
                var fighters=new[]{game.leftCombat,game.rightCombat};
                foreach(var f in fighters){f.battleSfx=null;f.battleVfx=null;f.hitEffect=null;}
                foreach(var source in fighters)
                foreach(var move in source.heavyCombatMoves)
                {
                    var target=fighters.Single(f=>f!=source);
                    foreach(var f in fighters)f.ResetCombat();
                    source.Animator.transform.position=Vector3.zero;target.Animator.transform.position=Vector3.right*move.attackRange;
                    source.ExecuteAttack(move,target);var pair=source.SourcePlayback;
                    var hits=game.battleVfx.timeline.FindMove(move).cues.Where(IsContactCue).ToArray();
                    var sheet=new Texture2D(1200,hits.Length*280,TextureFormat.RGB24,false);
                    var labels=new StringBuilder();
                    try
                    {
                        for(int hit=0;hit<hits.Length;hit++)
                        {
                            var rows=samples.Where(r=>r[0]==source.name && r[1]==move.moveName && r[2]==hit.ToString()).ToArray();
                            var bestWeapon=rows.Where(r=>r[4]=="Weapon" || r[4]=="Shield").OrderBy(r=>ParseContactFloat(r[5])).First();
                            var bestLimb=rows.Where(r=>r[4]!="Weapon" && r[4]!="Shield").OrderBy(r=>ParseContactFloat(r[5])).First();
                            var times=new[]{hits[hit].seconds,ParseContactFloat(bestWeapon[3]),ParseContactFloat(bestLimb[3])};
                            labels.AppendLine($"row {hit}: old {times[0]:F4}s; {bestWeapon[4]} {times[1]:F4}s/{bestWeapon[5]}m; {bestLimb[4]} {times[2]:F4}s/{bestLimb[5]}m");
                            for(int column=0;column<3;column++)
                            {
                                pair.EvaluateAt(times[column]);
                                CaptureContactPose(camera,fighters,pair,sheet,column*400,(hits.Length-hit-1)*280);
                            }
                        }
                        sheet.Apply();File.WriteAllBytes(HeavyContactReview+"/"+source.name+"_"+move.moveName+"_Candidates.png",sheet.EncodeToPNG());
                        File.WriteAllText(HeavyContactReview+"/"+source.name+"_"+move.moveName+"_Candidates.txt",labels.ToString());
                    }
                    finally{Object.DestroyImmediate(sheet);pair.Cancel();}
                }
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
        }

        static float ParseContactFloat(string value)=>float.Parse(value,CultureInfo.InvariantCulture);

        public static void InspectBattleHeavyContactProbe()
        {
            var scene=EditorSceneManager.OpenPreviewScene(SfxScene);var report=new StringBuilder();
            try
            {
                var game=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<GameManager>(true)).Single();
                var source=game.leftCombat;var target=game.rightCombat;var move=source.heavyCombatMoves[0];
                source.battleSfx=null;source.battleVfx=null;target.hitEffect=null;
                source.Animator.transform.position=Vector3.zero;target.Animator.transform.position=Vector3.right*move.attackRange;
                source.ExecuteAttack(move,target);var pair=source.SourcePlayback;
                using(var body=new ContactBody(target))
                using(var probe=new ContactProbe())
                foreach(float time in new[]{1.47833f,1.4783333f,1.47f,1.47833f})
                {
                    pair.EvaluateAt(time);body.Sample();
                    foreach(bool faces in new[]{false,true})
                    {
                        probe.Sample(pair,source,body,faces);
                        for(int kind=0;kind<2;kind++)
                        {
                            var point=probe.points[kind];float distance=body.Distance(point,out var nearest);var surface=body.Surface(point);
                            report.AppendLine($"t={time:R} faces={faces} {probe.names[kind]} score={probe.distances[kind]:R} actual={distance:R} point={point:F6} nearest={nearest:F6} surface={surface:F6} gap={Vector3.Distance(point,surface):R}");
                        }
                    }
                }
                pair.Cancel();
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);File.WriteAllText(HeavyContactReview+"/ProbeInspection.txt",report.ToString());}
        }

        public static void InspectBattleContactMeshes()
        {
            var scene=EditorSceneManager.OpenPreviewScene(SfxScene);var report=new StringBuilder();
            try
            {
                var game=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters=new[]{game.leftCombat,game.rightCombat};
                foreach(var f in fighters){f.battleSfx=null;f.battleVfx=null;f.hitEffect=null;}
                foreach(var source in fighters)
                {
                    var target=fighters.Single(f=>f!=source);var move=source.heavyCombatMoves[0];
                    foreach(var f in fighters)f.ResetCombat();
                    source.Animator.transform.position=Vector3.zero;target.Animator.transform.position=Vector3.right*move.attackRange;
                    source.ExecuteAttack(move,target);var pair=source.SourcePlayback;
                    foreach(float time in new[]{0,1.47f,2.9f})
                    {
                        pair.EvaluateAt(time);
                        foreach(var f in fighters)
                        {
                            report.AppendLine($"{source.name} attacks t={time} {f.name}: root={f.Animator.transform.position} hips={f.Animator.GetBoneTransform(HumanBodyBones.Hips).position}");
                            foreach(var skin in f.Animator.GetComponentsInChildren<SkinnedMeshRenderer>())
                            {
                                var mesh=new Mesh();
                                try
                                {
                                    skin.BakeMesh(mesh,false);
                                    report.AppendLine($"  {skin.name} count={mesh.vertexCount} scale={skin.transform.lossyScale} pos={skin.transform.position} rendered={skin.bounds}; false local={mesh.bounds} worldCenter={skin.transform.TransformPoint(mesh.bounds.center)}");
                                    skin.BakeMesh(mesh,true);report.AppendLine($"  true local={mesh.bounds} worldCenter={skin.transform.TransformPoint(mesh.bounds.center)}; rigidCenter={skin.transform.position+skin.transform.rotation*mesh.bounds.center}");
                                }
                                finally{Object.DestroyImmediate(mesh);}
                            }
                        }
                    }
                    pair.Cancel();
                }
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);File.WriteAllText(HeavyContactReview+"/MeshInspection.txt",report.ToString());}
        }

        static void CaptureContactPose(Camera camera,CharacterCombat[] fighters,FrankBattlePairPlayback pair,Texture2D sheet,int x,int y)
        {
            var skins=fighters.SelectMany(f=>f.Animator.GetComponentsInChildren<SkinnedMeshRenderer>())
                .Concat(pair.AttackerActor.GetComponentsInChildren<SkinnedMeshRenderer>()).Where(r=>r.enabled && r.sharedMesh && r.bones.Length>0).Distinct().ToArray();
            var clones=new List<GameObject>();var meshes=new List<Mesh>();
            var rt=RenderTexture.GetTemporary(400,280,24);var stamp=new Texture2D(400,280,TextureFormat.RGB24,false);
            var previous=RenderTexture.active;
            try
            {
                // PreviewScene skinned rendering may lag PlayableGraph sampling.
                // Render explicitly baked geometry from the current pose.
                foreach(var skin in skins)
                {
                    var mesh=new Mesh{hideFlags=HideFlags.HideAndDontSave};skin.BakeMesh(mesh,true);meshes.Add(mesh);
                    var clone=new GameObject("Contact pose mesh");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(clone,camera.gameObject.scene);clones.Add(clone);
                    clone.transform.SetPositionAndRotation(skin.transform.position,skin.transform.rotation);clone.transform.localScale=skin.transform.lossyScale;
                    clone.AddComponent<MeshFilter>().sharedMesh=mesh;clone.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;skin.enabled=false;
                }
                var anchors=fighters.SelectMany(f=>new[]{HumanBodyBones.Hips,HumanBodyBones.Head,HumanBodyBones.LeftHand,HumanBodyBones.RightHand,HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot}
                    .Select(b=>f.Animator.GetBoneTransform(b))).Where(b=>b).ToArray();
                var bounds=new Bounds(anchors[0].position,Vector3.zero);foreach(var b in anchors)bounds.Encapsulate(b.position);
                camera.transform.position=bounds.center+new Vector3(0,.25f,10);camera.transform.LookAt(bounds.center);
                camera.orthographicSize=Mathf.Max(.85f,bounds.extents.y+.25f,(bounds.extents.x+.3f)/(400f/280));
                camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;stamp.ReadPixels(new Rect(0,0,400,280),0,0);stamp.Apply();
                sheet.SetPixels(x,y,400,280,stamp.GetPixels());
            }
            finally
            {
                camera.targetTexture=null;RenderTexture.active=previous;
                foreach(var skin in skins)if(skin)skin.enabled=true;
                foreach(var clone in clones)Object.DestroyImmediate(clone);foreach(var mesh in meshes)Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(stamp);RenderTexture.ReleaseTemporary(rt);
            }
        }

        [MenuItem("Tools/Battle/Apply reviewed heavy contact timing")]
        public static void InstallBattleHeavyContacts()
        {
            if (File.Exists(AllHeavyReview + "/ApprovedSelection.csv")) { InstallAllReviewedHeavyContacts(); return; }
            var selections=File.ReadAllLines(HeavyContactReview+"/ContactSelection.csv").Skip(1).Where(l=>!string.IsNullOrWhiteSpace(l)).Select(l=>l.Split(',')).ToArray();
            var report=new StringBuilder();
            var scene=EditorSceneManager.OpenPreviewScene(SfxScene);
            try
            {
                var game=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<GameManager>(true)).Single();
                var bank=game.battleVfx.timeline;
                foreach(var source in new[]{game.leftCombat,game.rightCombat})
                foreach(var move in source.heavyCombatMoves)
                {
                    var rows=selections.Where(r=>r[0]==source.name && r[1]==move.moveName).ToArray();
                    if(rows.Length==0)throw new Exception("Missing reviewed contacts: "+source.name+"/"+move.moveName);
                    var profile=bank.FindMove(move);
                    foreach(var cue in profile.cues)
                    {
                        cue.hasContactPoint=false;
                        cue.damageOnLanding=move.moveName=="Heavy_Assassin" && cue.finalLanding && cue.group=="body_fall";
                    }
                    var originalHits=profile.cues.Where(IsContactCue).ToArray();
                    if(move.moveName=="Heavy_Katana" && originalHits.Length==3 && rows.Length==2)
                    {
                        var phantom=originalHits[2];var sweep=profile.cues.Where(c=>c.seconds<phantom.seconds && c.group.EndsWith("swing",StringComparison.Ordinal)).Last();
                        profile.cues=profile.cues.Where(c=>c!=phantom && c!=sweep).ToArray();originalHits=originalHits.Take(2).ToArray();
                        report.AppendLine(source.name+"/Heavy_Katana: removed false impact/slash while sheathing the blade; damage uses the two real cuts.");
                    }
                    if(rows.Length!=originalHits.Length)throw new Exception("Contact count changed without reviewing the combo.");
                    var target=source==game.leftCombat?game.rightCombat:game.leftCombat;
                    foreach(var f in new[]{source,target})f.ResetCombat();
                    Vector3 sourcePosition=source.Animator.transform.position,targetPosition=target.Animator.transform.position;
                    Quaternion sourceRotation=source.Animator.transform.rotation,targetRotation=target.Animator.transform.rotation;
                    source.Animator.transform.position=Vector3.zero;target.Animator.transform.position=Vector3.right*move.attackRange;
                    var savedSfx=source.battleSfx;var savedVfx=source.battleVfx;var savedHit=target.hitEffect;
                    source.battleSfx=null;source.battleVfx=null;target.hitEffect=null;
                    try
                    {
                        source.ExecuteAttack(move,target);var pair=source.SourcePlayback;
                        using(var body=new ContactBody(target))
                        using(var probe=new ContactProbe())
                        foreach(var row in rows)
                        {
                            int index=int.Parse(row[2]);var cue=originalHits[index];float old=cue.seconds,time=ParseContactFloat(row[3]);
                            var swing=profile.cues.Where(c=>c.seconds<old && c.group.EndsWith("swing",StringComparison.Ordinal)).LastOrDefault();
                            if(swing!=null)swing.seconds=Mathf.Max(0,swing.seconds+time-old);
                            cue.seconds=time;cue.contactSource=row[4];
                            pair.EvaluateAt(time);body.Sample();probe.Sample(pair,source,body,true);
                            int kind=Array.IndexOf(probe.names,row[4]);if(kind<0)throw new Exception("Unknown striker "+row[4]);
                            Vector3 contact=body.Surface(probe.points[kind]);
                            var bone=NearestContactBone(target,contact);var anchor=target.Animator.GetBoneTransform(bone);
                            cue.hasContactPoint=true;cue.contactBone=bone;cue.contactOffset=anchor.InverseTransformPoint(contact);
                            report.AppendLine(FormattableString.Invariant($"{source.name}/{move.moveName} #{index}: {old:F4}s -> {time:F4}s; {row[4]} -> {bone}; surface gap={Vector3.Distance(contact,probe.points[kind]):F4}m"));
                        }
                        pair.Cancel();profile.cues=profile.cues.OrderBy(c=>c.seconds).ToArray();
                    }
                    finally
                    {
                        source.SourcePlayback?.Cancel();source.battleSfx=savedSfx;source.battleVfx=savedVfx;target.hitEffect=savedHit;
                        source.Animator.transform.SetPositionAndRotation(sourcePosition,sourceRotation);target.Animator.transform.SetPositionAndRotation(targetPosition,targetRotation);
                    }
                }
                EditorUtility.SetDirty(bank);
                AssetDatabase.SaveAssets();
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
            File.WriteAllText(HeavyContactReview+"/Installation.txt",report.ToString());
        }

        static HumanBodyBones NearestContactBone(CharacterCombat target,Vector3 contact)
        {
            HumanBodyBones best=HumanBodyBones.Chest;float distance=float.PositiveInfinity;
            foreach(HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if(bone>=HumanBodyBones.LastBone)continue;
                var anchor=target.Animator.GetBoneTransform(bone);if(!anchor)continue;
                float d=(anchor.position-contact).sqrMagnitude;if(d<distance){distance=d;best=bone;}
            }
            return best;
        }

        public static void ValidateBattleHeavyContacts()
        {
            var scene=EditorSceneManager.OpenPreviewScene(SfxScene);var report=new StringBuilder();int cases=0,checkedHits=0;
            try
            {
                var game=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters=new[]{game.leftCombat,game.rightCombat};var vfx=game.battleVfx;
                foreach(var source in fighters)
                foreach(var move in source.heavyCombatMoves)
                foreach(bool mirrored in new[]{false,true})
                foreach(bool skipped in new[]{false,true})
                {
                    foreach(var f in fighters)f.ResetCombat();vfx.ResetForMatch();game.battleSfx.ResetForMatch();
                    var target=fighters.Single(f=>f!=source);
                    source.Animator.transform.position=Vector3.zero;target.Animator.transform.position=Vector3.right*move.attackRange*(mirrored?-1:1);
                    var profile=vfx.timeline.FindMove(move);
                    if(profile.cues.Where(IsContactCue).Any(c=>!c.hasContactPoint))throw new Exception("Unreviewed heavy hit: "+move.moveName);
                    if(!source.ExecuteAttack(move,target))throw new Exception("Heavy contact validation rejected attack.");
                    var pair=source.SourcePlayback;var contacts=BattleHitDamageSequence.ContactTimes(profile,pair.Duration);
                    long hp=1000,sum=0;int damageHits=0,effects=0;
                    var damage=new BattleHitDamageSequence(1000,897,103,contacts,(value,amount)=>{hp=value;sum+=amount;damageHits++;});
                    Action<FrankBattlePairPlayback,float> advance=(owner,time)=>damage.Advance(time);
                    pair.TimelineAdvanced+=advance;
                    using(var body=new ContactBody(target))
                    using(var probe=new ContactProbe())
                    {
                        Action<string,GameObject> observe=(id,root)=>
                        {
                            if(id!="heavy_hit" && id!="light_hit")return;
                            var cue=profile.cues.Single(c=>IsContactCue(c) && Mathf.Abs(c.seconds-pair.SampleTime)<.0001f);
                            int reached=contacts.Count(t=>t<=cue.seconds);
                            long expected=1000-(103/contacts.Length*reached+Math.Max(0,reached-(contacts.Length-103%contacts.Length)));
                            if(damageHits!=reached || hp!=expected)throw new Exception("VFX emitted before its damage: "+move.moveName);
                            var bone=target.Animator.GetBoneTransform(cue.contactBone);var contact=bone.TransformPoint(cue.contactOffset);
                            body.Sample();probe.Sample(pair,source,body,true);int kind=Array.IndexOf(probe.names,cue.contactSource);
                            float surfaceError=Vector3.Distance(contact,body.Surface(contact));
                            float gap=Vector3.Distance(body.Surface(probe.points[kind]),probe.points[kind]);
                            if(surfaceError>.005f)throw new Exception("Impact point is off the posed body: "+source.name+"/"+move.moveName+" error="+surfaceError);
                            if(gap>.05f)throw new Exception("Strike does not touch the receiver: "+source.name+"/"+move.moveName+" gap="+gap);
                            if(Vector3.Distance(root.transform.position,contact)>.016f)throw new Exception("Impact is displaced from contact.");
                            report.AppendLine(FormattableString.Invariant($"CONTACT {source.name}/{move.moveName} {cue.seconds:F4}s {cue.contactSource}: surface error {surfaceError:F5}m; striker gap {gap:F5}m; HP {hp}; mirrored={mirrored}, skipped={skipped}"));
                            effects++;checkedHits++;
                        };
                        vfx.EffectPlayed+=observe;
                        try
                        {
                            if(skipped)pair.AdvanceTo(pair.Duration);
                            else foreach(float contact in contacts)
                            {
                                pair.AdvanceTo(contact-.0001f);int before=damageHits;
                                pair.AdvanceTo(contact);
                                if(damageHits!=before+1)throw new Exception("Damage did not occur at contact.");
                                int count=effects;pair.AdvanceTo(contact);pair.AdvanceTo(contact-.2f);
                                if(effects!=count || damageHits!=before+1)throw new Exception("Repeated contact.");
                            }
                            pair.AdvanceTo(pair.Duration);
                            if(sum!=103 || hp!=897 || damageHits!=contacts.Length || effects!=profile.cues.Count(IsContactCue))throw new Exception("Missing heavy contact or total damage.");
                            int saved=effects;pair.Cancel();pair.AdvanceTo(pair.Duration);
                            if(effects!=saved)throw new Exception("Cancelled combo emitted VFX.");
                        }
                        finally{vfx.EffectPlayed-=observe;pair.TimelineAdvanced-=advance;damage.Cancel();pair.Cancel();}
                    }
                    cases++;
                }
                report.AppendLine($"ALL PASS: {cases} heavy/mirror/frame-skip cases; {checkedHits} surface contacts; damage before impact on the same contact pose; exact totals; no duplicate, rewind or cancelled effects.");
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);File.WriteAllText(HeavyContactReview+"/Validation.txt",report.ToString());}
        }

        public static void RefineBattleHeavyContacts()
        {
            var plan=File.ReadAllLines(HeavyContactReview+"/ContactPlan.csv").Skip(1).Select(l=>l.Split(',')).ToArray();
            var selection=new StringBuilder("fighter,move,hit,time,kind\n");var report=new StringBuilder();
            var scene=EditorSceneManager.OpenPreviewScene(SfxScene);
            try
            {
                var game=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters=new[]{game.leftCombat,game.rightCombat};
                foreach(var f in fighters){f.battleVfx=null;f.battleSfx=null;f.hitEffect=null;}
                foreach(var source in fighters)
                foreach(var move in source.heavyCombatMoves)
                {
                    var target=fighters.Single(f=>f!=source);foreach(var f in fighters)f.ResetCombat();
                    source.Animator.transform.position=Vector3.zero;target.Animator.transform.position=Vector3.right*move.attackRange;
                    source.ExecuteAttack(move,target);var pair=source.SourcePlayback;
                    using(var body=new ContactBody(target))
                    using(var probe=new ContactProbe())
                    foreach(var row in plan.Where(r=>r[0]==source.name && r[1]==move.moveName))
                    {
                        float center=ParseContactFloat(row[3]);int kind=Array.IndexOf(probe.names,row[4]);
                        var times=new List<float>();var distances=new List<float>();
                        for(int frame=-14;frame<=10;frame++)
                        {
                            float time=center+frame/120f;pair.EvaluateAt(time);body.Sample();probe.Sample(pair,source,body,true);
                            times.Add(time);distances.Add(probe.distances[kind]);
                        }
                        float minimum=distances.Min(),threshold=Mathf.Max(.012f,minimum+.004f);
                        int sample=distances.FindIndex(d=>d<=threshold);
                        float selected=times[sample];
                        selection.AppendLine(FormattableString.Invariant($"{source.name},{move.moveName},{row[2]},{selected:F5},{row[4]}"));
                        report.AppendLine(FormattableString.Invariant($"{source.name}/{move.moveName} #{row[2]} {row[4]}: {center:F4}s -> first surface entry {selected:F5}s; min distance={minimum:F5}m"));
                        File.WriteAllText(HeavyContactReview+"/Refinement.txt",report.ToString());
                    }
                    pair.Cancel();
                }
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);File.WriteAllText(HeavyContactReview+"/ContactSelection.csv",selection.ToString());}
        }
    }
}
