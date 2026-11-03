using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Playables;
using UnityEngine.Animations;

public class NeonVelocityStarter : MonoBehaviour
{
    [Header("Gameplay")]
    public float forwardSpeed = 18f;
    public float maxSpeed = 38f;
    public float speedGain = 0.25f;
    public float laneDistance = 2.5f;
    public float laneChangeSpeed = 16f;
    public float jumpVelocity = 12f;
    public float gravity = -26f;
    public int coinScoreValue = 10;
    public float coinMagnetRadius = 2.2f;

    [Header("Procedural Generation")]
    public int poolLength = 150;
    public float chunkLength = 15f;
    float spawnZ = 10f;
    int chunkIndex;

    [Header("Character")]
    public int skinIndex = 0;
    public float runAnimSpeed = 24f;   // stride frequency (scaled with speed)
    public float runSwing = 72f;       // leg swing angle
    public float kneeBend = 115f;      // how high the heels kick up
    public float forwardLean = 22f;    // sprint lean

    [Header("Look")]
    public bool enableBloom = true;
    public bool showMoveButtons = false;
    public bool showSkinButton = false;

    [Header("Optional 3D Character")]
    public GameObject characterPrefab;
    public AnimationClip runClip;
    public AnimationClip jumpClip;
    public float runClipSpeed = 1.3f;
    public float modelScale = 1f;
    public float modelYOffset = 0f;

    class SkinPreset
    {
        public string name;
        public Color skin, hair, shirt, pants, shoes, accent, sleeve, detail;
        public SkinPreset(string n, Color sk, Color ha, Color sh, Color pa, Color so, Color ac, Color sl, Color de)
        { name=n; skin=sk; hair=ha; shirt=sh; pants=pa; shoes=so; accent=ac; sleeve=sl; detail=de; }
    }

    // Multi-colour palettes: body, sleeves, pants, shoes, hood/stripes and glow accents all differ.
    //                          name       skin                      hair                     shirt                    pants                    shoes                    accent                   sleeve                   detail
    static readonly SkinPreset[] skins =
    {
        new SkinPreset("BOY",     new Color(.95f,.72f,.58f), new Color(.3f,.16f,.08f),  new Color(.62f,.2f,.01f),   new Color(.03f,.06f,.26f),  new Color(.1f,.09f,.12f),  new Color(.9f,.5f,.15f),  new Color(.48f,.15f,.01f),  new Color(.95f,.8f,.55f)),
        new SkinPreset("CRIMSON", new Color(.9f,.68f,.55f),  new Color(.08f,.05f,.05f), new Color(.65f,.08f,.1f),  new Color(.1f,.1f,.14f),   new Color(.85f,.78f,.62f), new Color(.9f,.7f,.4f),   new Color(.4f,.05f,.07f),  new Color(.9f,.75f,.5f)),
        new SkinPreset("FOREST",  new Color(.85f,.62f,.48f), new Color(.15f,.1f,.06f),  new Color(.1f,.4f,.25f),   new Color(.22f,.17f,.12f), new Color(.08f,.08f,.08f), new Color(.8f,.6f,.2f),   new Color(.06f,.25f,.15f), new Color(.85f,.75f,.5f)),
        new SkinPreset("ROYAL",   new Color(.7f,.48f,.35f),  new Color(.06f,.05f,.05f), new Color(.15f,.2f,.6f),   new Color(.1f,.1f,.16f),   new Color(.9f,.85f,.75f),  new Color(.9f,.75f,.3f),  new Color(.08f,.12f,.4f),  new Color(.9f,.8f,.5f)),
        new SkinPreset("SAND",    new Color(.95f,.75f,.62f), new Color(.45f,.3f,.15f),  new Color(.85f,.7f,.45f),  new Color(.25f,.2f,.3f),   new Color(.12f,.1f,.1f),   new Color(.7f,.45f,.25f), new Color(.65f,.5f,.3f),   new Color(.95f,.9f,.7f)),
        new SkinPreset("STEEL",   new Color(.9f,.72f,.6f),   new Color(.2f,.2f,.25f),   new Color(.35f,.38f,.45f), new Color(.08f,.08f,.1f),  new Color(1f,.45f,.08f),   new Color(1f,.5f,.1f),    new Color(.2f,.22f,.28f),  new Color(.8f,.4f,.1f))
    };

    CharacterController player;
    Camera gameCamera;
    int lane = 1;
    float verticalSpeed, currentSpeed, distance, runPhase;
    int coins;
    float scorePulse, popupTimer;
    Text scorePopupText;
    Outline scoreOutline, infoOutline;
    int lastStepIndex=-1;
    public Vector3 PlayerPosition => player != null ? player.transform.position : Vector3.zero;
    public float CurrentSpeed => currentSpeed;
    bool paused, gameOver, usingPrefab, onGround = true;
    int Score => Mathf.Max(0, Mathf.FloorToInt(distance)) + coins * coinScoreValue;

    Transform model, leftArm, rightArm, leftLeg, rightLeg, leftShin, rightShin, ponytail;
    Material skinMat, hairMat, shirtMat, pantsMat, shoeMat, accentMat, sleeveMat, detailMat, eyeMat, dustMat;
    Material roadMat, cyanMat, pinkMat, goldMat, darkMat, buildingMat, gridMat, xMat, xGlowMat;
    Material coinBodyMat, coinStarMat, auraMat;
    Material[] neonMats, windowMats;
    Mesh coinMesh;
    Transform roadRoot, moonTransform, starsRoot;

    PlayableGraph graph;
    AnimationClipPlayable runPlayable, jumpPlayable;
    AnimationMixerPlayable mixer;
    bool graphReady;
    float jumpBuffer;
    float runClock, jumpClock;

    Text scoreText, coinText, distanceText, messageText, pauseText, skinButtonText;
    Text goScoreText;
    GameObject gameOverPanel;
    readonly List<GameObject> activeWorldObjects = new List<GameObject>();

    void Start()
    {
        Time.timeScale = 1f;
        currentSpeed = forwardSpeed;
        skinIndex = Mathf.Abs(PlayerPrefs.GetInt("NV_Skin3", 0)) % skins.Length;

        CreateMaterials();
        CreateWorldBase();
        CreatePlayer();
        CreateCamera();
        SetupGlow();
        CreateUI();
        ApplySkin(skinIndex);

        spawnZ = 10f;
        for (int i = 0; i < 15; i++) SpawnChunk();
        RefreshHud();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R)) { RestartGame(); return; }
        if (gameOver) return;
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P)) TogglePause();
        if (paused) return;

        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) ChangeLane(-1);
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) ChangeLane(1);
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.UpArrow)) Jump();
        if (Input.GetKeyDown(KeyCode.C)) CycleSkin();
        if (player == null) return;

        currentSpeed = Mathf.Min(maxSpeed, currentSpeed + speedGain * Time.deltaTime);
        if (player.isGrounded && verticalSpeed < 0f) verticalSpeed = -2f;
        else verticalSpeed += gravity * Time.deltaTime;

        float targetX = (lane - 1) * laneDistance;
        float xDiff = targetX - player.transform.position.x;
        player.Move(new Vector3(xDiff * laneChangeSpeed, verticalSpeed, currentSpeed) * Time.deltaTime);
        distance = player.transform.position.z;

        // Stable ground check (CharacterController.isGrounded flickers, which made the run cycle stutter into a "walk").
        onGround = player.isGrounded || (player.transform.position.y <= 1.12f && verticalSpeed <= 0.5f);

        // Jump buffer: a press just before landing still jumps as soon as the runner touches down.
        jumpBuffer = Mathf.Max(0f, jumpBuffer - Time.deltaTime);
        if(jumpBuffer > 0f && onGround) { verticalSpeed = jumpVelocity; jumpBuffer = 0f; }

        AnimateRunner(xDiff);
        while (player.transform.position.z + poolLength > spawnZ) SpawnChunk();
        CleanupOldObjects();

        if (roadRoot != null) roadRoot.position = new Vector3(0, 0, player.transform.position.z - 50f);
        if (moonTransform != null) moonTransform.position = new Vector3(0, 32, player.transform.position.z + 150f);
        RefreshHud();
        UpdateHudFx();
    }

    void LateUpdate()
    {
        if (player == null || gameCamera == null) return;
        Vector3 wanted = player.transform.position + new Vector3(0, 4.5f, -7.5f);
        gameCamera.transform.position = Vector3.Lerp(gameCamera.transform.position, wanted, 10f * Time.deltaTime);
        gameCamera.transform.LookAt(player.transform.position + new Vector3(0, 1f, 6f));
        float sp = Mathf.InverseLerp(forwardSpeed, maxSpeed, currentSpeed);
        gameCamera.fieldOfView = Mathf.Lerp(gameCamera.fieldOfView, Mathf.Lerp(65f, 80f, sp), 4f * Time.deltaTime);
        if (starsRoot != null) starsRoot.position = gameCamera.transform.position;
    }

    void OnDestroy()
    {
        if (graphReady && graph.IsValid()) graph.Destroy();
    }

    Material MakeMaterial(Color color, bool glow)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material mat = new Material(shader);
        SetMatColor(mat, color, glow ? 1.6f : 0f);
        return mat;
    }

    void SetMatColor(Material mat, Color color, float emission)
    {
        if (mat == null) return;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (emission > 0f)
        {
            mat.EnableKeyword("_EMISSION");
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", color * emission);
        }
        else if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", Color.black);
    }

    Material MakeGlowMat(Color color, float intensity)
    {
        Material m = MakeMaterial(color, false);
        SetMatColor(m, color, intensity);
        return m;
    }

    void SetMetal(Material m, float metallic, float smoothness)
    {
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
    }

    Material NeonAt(int i) => neonMats[((i % neonMats.Length) + neonMats.Length) % neonMats.Length];
    Material RandomNeon() => neonMats[Random.Range(0, neonMats.Length)];
    Material RandomWindow() => windowMats[Random.Range(0, windowMats.Length)];

    void CreateMaterials()
    {
        // Cohesive synthwave palette: deep indigo world, cyan + magenta + violet accents only.
        roadMat = MakeMaterial(new Color(.06f,.04f,.14f), false);          // darker road
        SetMetal(roadMat, 0f, .45f);
        cyanMat = MakeMaterial(new Color(0f,.85f,1f), true);
        pinkMat = MakeMaterial(new Color(1f,.1f,.55f), true);
        goldMat = MakeMaterial(new Color(1f,.7f,.1f), true);
        SetMatColor(goldMat, new Color(1f,.7f,.1f), 3f);
        darkMat = MakeMaterial(new Color(.02f,.01f,.05f), false);
        buildingMat = MakeMaterial(new Color(.04f,.03f,.11f), false);      // darker buildings

        neonMats = new Material[]
        {
            cyanMat, pinkMat,
            MakeMaterial(new Color(.55f,.2f,1f), true),
            MakeMaterial(new Color(.15f,.4f,1f), true)
        };
        // Soft, dimmer window lights so buildings feel alive but not noisy.
        windowMats = new Material[]
        {
            MakeGlowMat(new Color(.25f,.7f,1f), .8f),
            MakeGlowMat(new Color(.8f,.3f,.9f), .8f),
            MakeGlowMat(new Color(.5f,.4f,1f), .8f)
        };
        gridMat = MakeGlowMat(new Color(.45f,.2f,1f), 1.4f);
        xMat = MakeMaterial(new Color(.01f,.005f,.02f), false);          // black X
        xGlowMat = MakeGlowMat(new Color(1f,.55f,.95f), 2.2f);          // glowing rim behind the black X

        // Golden coins: rich gold face + deeper-gold star, gold self-glow so they are always clearly golden
        coinBodyMat = MakeMaterial(new Color(1f,.78f,.18f), false);
        SetMatColor(coinBodyMat, new Color(1f,.78f,.18f), .85f);
        SetMetal(coinBodyMat, .45f, .75f);
        coinStarMat = MakeMaterial(new Color(1f,.62f,.08f), false);
        SetMatColor(coinStarMat, new Color(1f,.62f,.08f), .6f);
        SetMetal(coinStarMat, .45f, .6f);
        coinMesh = BuildCoinMesh();

        auraMat = MakeMaterial(Color.white, false);
        skinMat = MakeMaterial(Color.white, false);
        hairMat = MakeMaterial(Color.white, false);
        shirtMat = MakeMaterial(Color.white, false);
        pantsMat = MakeMaterial(Color.white, false);
        shoeMat = MakeMaterial(Color.white, true);
        accentMat = MakeMaterial(Color.white, true);
        sleeveMat = MakeMaterial(Color.white, false);
        detailMat = MakeMaterial(Color.white, false);
        eyeMat = MakeMaterial(Color.white, false);
        dustMat = MakeMaterial(new Color(.75f,.7f,.85f), false);
        SetMatColor(dustMat, new Color(.75f,.7f,.85f), .3f);
        SetMatColor(eyeMat, Color.white, .15f);
        foreach(Material cm in new Material[]{skinMat,hairMat,shirtMat,pantsMat,shoeMat,accentMat,sleeveMat,detailMat,eyeMat}) SetMetal(cm,0f,.15f);
    }

    // Small procedural environment map: dark violet room with warm soft-box strips.
    // Spinning metal coins catch these strips, which gives the real "glint" of gold.
    Cubemap BuildStudioCube()
    {
        const int n=32;
        Cubemap cm=new Cubemap(n,TextureFormat.RGBA32,false);
        Color dark=new Color(.06f,.04f,.12f), mid=new Color(.45f,.33f,.45f), warm=new Color(1f,.88f,.6f), sky=new Color(.95f,.9f,.85f);
        Color[] px=new Color[n*n];
        CubemapFace[] sides={CubemapFace.PositiveX,CubemapFace.NegativeX,CubemapFace.PositiveZ,CubemapFace.NegativeZ};
        foreach(CubemapFace f in sides)
        {
            for(int y=0;y<n;y++)
                for(int x=0;x<n;x++)
                {
                    float u=x/(n-1f), v=y/(n-1f);
                    float band=Mathf.Clamp01(1f-Mathf.Abs(v-.5f)*3f);
                    float strip=Mathf.Clamp01(1f-Mathf.Abs(u-.5f)*7f)+.5f*Mathf.Clamp01(1f-Mathf.Abs(u-.18f)*10f)+.5f*Mathf.Clamp01(1f-Mathf.Abs(u-.82f)*10f);
                    Color c=Color.Lerp(dark,mid,band);
                    c+=warm*strip*band;
                    px[y*n+x]=new Color(Mathf.Clamp01(c.r),Mathf.Clamp01(c.g),Mathf.Clamp01(c.b),1f);
                }
            cm.SetPixels(px,f);
        }
        for(int y=0;y<n;y++)
            for(int x=0;x<n;x++)
            {
                float r=Mathf.Clamp01(Vector2.Distance(new Vector2(x/(n-1f),y/(n-1f)),new Vector2(.5f,.5f))*1.6f);
                px[y*n+x]=Color.Lerp(sky,mid,r);
            }
        cm.SetPixels(px,CubemapFace.PositiveY);
        for(int i=0;i<px.Length;i++) px[i]=dark;
        cm.SetPixels(px,CubemapFace.NegativeY);
        cm.Apply();
        return cm;
    }

    Mesh BuildCoinMesh()
    {
        const int N = 32;
        List<Vector3> verts = new List<Vector3>();
        List<Vector3> norms = new List<Vector3>();
        List<int> bodyTris = new List<int>();
        List<int> starTris = new List<int>();
        float[,] prof = {
            {0f,.045f},{.34f,.045f},{.37f,.07f},{.45f,.07f},{.5f,.04f},
            {.5f,-.04f},{.45f,-.07f},{.37f,-.07f},{.34f,-.045f},{0f,-.045f}
        };

        for (int e=0; e<prof.GetLength(0)-1; e++)
        {
            float r0=prof[e,0], z0=prof[e,1], r1=prof[e+1,0], z1=prof[e+1,1];
            float dr=r1-r0, dz=z1-z0, len=Mathf.Sqrt(dr*dr+dz*dz);
            float nr=-dz/len, nz=dr/len;
            int start=verts.Count;
            for (int i=0; i<=N; i++)
            {
                float a=i*Mathf.PI*2f/N, c=Mathf.Cos(a), s=Mathf.Sin(a);
                verts.Add(new Vector3(r0*c,r0*s,z0)); norms.Add(new Vector3(nr*c,nr*s,nz));
                verts.Add(new Vector3(r1*c,r1*s,z1)); norms.Add(new Vector3(nr*c,nr*s,nz));
            }
            for (int i=0; i<N; i++)
            {
                int a=start+i*2, b=a+1, c=a+2, d=a+3;
                bodyTris.Add(a); bodyTris.Add(d); bodyTris.Add(b);
                bodyTris.Add(a); bodyTris.Add(c); bodyTris.Add(d);
            }
        }

        Vector2[] star = new Vector2[10];
        for (int k=0;k<10;k++)
        {
            float r=(k%2==0)?.2f:.085f, a=Mathf.PI/2f+k*Mathf.PI/5f;
            star[k]=new Vector2(r*Mathf.Cos(a),r*Mathf.Sin(a));
        }
        for (int sign=1;sign>=-1;sign-=2)
        {
            float zb=.045f*sign, zt=.065f*sign;
            int center=verts.Count;
            verts.Add(new Vector3(0,0,zt)); norms.Add(new Vector3(0,0,sign));
            for (int k=0;k<10;k++) { verts.Add(new Vector3(star[k].x,star[k].y,zt)); norms.Add(new Vector3(0,0,sign)); }
            for (int k=0;k<10;k++)
            {
                int p0=center+1+k, p1=center+1+(k+1)%10;
                if(sign>0){starTris.Add(center);starTris.Add(p1);starTris.Add(p0);}
                else{starTris.Add(center);starTris.Add(p0);starTris.Add(p1);}
            }
            for(int k=0;k<10;k++)
            {
                Vector2 a=star[k], b=star[(k+1)%10], edge=b-a;
                Vector3 normal=new Vector3(edge.y,-edge.x,0).normalized;
                int j=verts.Count;
                verts.Add(new Vector3(a.x,a.y,zb)); norms.Add(normal);
                verts.Add(new Vector3(b.x,b.y,zb)); norms.Add(normal);
                verts.Add(new Vector3(a.x,a.y,zt)); norms.Add(normal);
                verts.Add(new Vector3(b.x,b.y,zt)); norms.Add(normal);
                if(sign>0)
                {
                    starTris.Add(j);starTris.Add(j+3);starTris.Add(j+1);
                    starTris.Add(j);starTris.Add(j+2);starTris.Add(j+3);
                }
                else
                {
                    starTris.Add(j);starTris.Add(j+1);starTris.Add(j+3);
                    starTris.Add(j);starTris.Add(j+3);starTris.Add(j+2);
                }
            }
        }
        Mesh mesh=new Mesh { name="Neon Coin" };
        mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.subMeshCount=2;
        mesh.SetTriangles(bodyTris,0); mesh.SetTriangles(starTris,1); mesh.RecalculateBounds();
        return mesh;
    }

    GameObject MakeCube(string objectName, Vector3 position, Vector3 scale, Material material)
    {
        GameObject obj=GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name=objectName; obj.transform.position=position;
        obj.transform.localScale=scale; obj.GetComponent<Renderer>().material=material;
        return obj;
    }

    void CreateWorldBase()
    {
        RenderSettings.skybox=null;
        RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight=new Color(.02f,.015f,.05f);   // darker ambient
        RenderSettings.defaultReflectionMode=DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture=BuildStudioCube();
        RenderSettings.reflectionIntensity=.6f;                 // less reflected light
        RenderSettings.fog=true;
        RenderSettings.fogMode=FogMode.ExponentialSquared;
        RenderSettings.fogColor=new Color(.01f,.005f,.03f);       // near-black violet haze
        RenderSettings.fogDensity=.018f;

        foreach(Light light in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if(light.type==LightType.Directional)
            {
                light.intensity=.12f;                            // dim sun
                light.color=new Color(.45f,.4f,.9f);
                light.transform.rotation=Quaternion.Euler(50f,-25f,0f);
            }
        }

        starsRoot=new GameObject("Stars").transform;
        Material starA=MakeGlowMat(new Color(.6f,.6f,1f),1f);
        Material starB=MakeGlowMat(new Color(.25f,.6f,1f),1.1f);
        for(int i=0;i<120;i++)
        {
            Vector3 dir=Random.onUnitSphere;
            dir.y=.35f+Mathf.Abs(dir.y)*.65f; dir.Normalize();
            GameObject star=GameObject.CreatePrimitive(PrimitiveType.Cube);
            star.name="Star"; Destroy(star.GetComponent<Collider>());
            star.transform.SetParent(starsRoot,false);
            star.transform.localPosition=dir*Random.Range(95f,110f);
            star.transform.localScale=Vector3.one*Random.Range(.15f,.4f);
            star.GetComponent<Renderer>().material=Random.value>.3f?starA:starB;
        }

        roadRoot=new GameObject("Road Root").transform;
        MakeRoadPart("Neon Road Base",new Vector3(0,-.25f,500),new Vector3(9,.5f,1000),roadMat);
        MakeRoadPart("Left Pink Line",new Vector3(-1.25f,.025f,500),new Vector3(.05f,.025f,1000),pinkMat);
        MakeRoadPart("Right Pink Line",new Vector3(1.25f,.025f,500),new Vector3(.05f,.025f,1000),pinkMat);
        MakeRoadPart("Left Cyan Edge",new Vector3(-4.35f,.03f,500),new Vector3(.12f,.06f,1000),cyanMat);
        MakeRoadPart("Right Cyan Edge",new Vector3(4.35f,.03f,500),new Vector3(.12f,.06f,1000),cyanMat);

        GameObject moon=GameObject.CreatePrimitive(PrimitiveType.Sphere);
        moon.name="Neon Moon"; moon.transform.position=new Vector3(0,32,150);
        moon.transform.localScale=Vector3.one*20f;
        moon.GetComponent<Renderer>().material=MakeGlowMat(new Color(.55f,.12f,.45f),.4f);
        Destroy(moon.GetComponent<Collider>()); moonTransform=moon.transform;
    }

    void MakeRoadPart(string n,Vector3 pos,Vector3 scale,Material mat)
    {
        GameObject g=MakeCube(n,pos,scale,mat);
        g.transform.SetParent(roadRoot,false); g.transform.localPosition=pos;
    }

    void SpawnChunk()
    {
        for(int g=0;g<2;g++)
        {
            GameObject line=MakeCube("Road Grid Light",new Vector3(0,.015f,spawnZ+g*chunkLength*.5f),new Vector3(8.7f,.025f,.045f),gridMat);
            Destroy(line.GetComponent<Collider>()); activeWorldObjects.Add(line);
        }

        if(spawnZ>25f && Random.value>.25f)
        {
            int obstacleLane=Random.Range(0,3);
            float ox=(obstacleLane-1)*laneDistance;
            GameObject obstacle=MakeCube("Pink Obstacle",new Vector3(ox,.75f,spawnZ),new Vector3(1.8f,1.5f,1.3f),pinkMat);
            BoxCollider obc=obstacle.GetComponent<BoxCollider>(); obc.isTrigger=true;
            obc.size=new Vector3(.9f,.85f,.8f); obc.center=new Vector3(0,-.075f,0);   // forgiving hitbox, bottom stays on the road
            Rigidbody orb=obstacle.AddComponent<Rigidbody>(); orb.isKinematic=true; orb.useGravity=false;
            obstacle.AddComponent<NeonVelocityObstacle>().manager=this;
            activeWorldObjects.Add(obstacle);

            for(int k=0;k<2;k++)
            {
                GameObject glow=MakeCube("Obstacle X Glow",new Vector3(ox,.75f,spawnZ-.665f),new Vector3(1.78f,.26f,.03f),xGlowMat);
                glow.transform.rotation=Quaternion.Euler(0,0,k==0?40f:-40f);
                Destroy(glow.GetComponent<Collider>()); activeWorldObjects.Add(glow);

                GameObject bar=MakeCube("Obstacle X",new Vector3(ox,.75f,spawnZ-.685f),new Vector3(1.6f,.13f,.03f),xMat);
                bar.transform.rotation=Quaternion.Euler(0,0,k==0?40f:-40f);
                Destroy(bar.GetComponent<Collider>()); activeWorldObjects.Add(bar);
            }

            int coinLane=(obstacleLane+Random.Range(1,3))%3;
            float cx=(coinLane-1)*laneDistance;
            bool arcCoins=Random.value>.5f;
            for(int i=0;i<5;i++)
            {
                float arc=Mathf.Sin(i/4f*Mathf.PI);
                GameObject coin=new GameObject("Collectible Coin");
                coin.transform.position=new Vector3(cx,1.3f+(arcCoins?arc*1.1f:0f),spawnZ-1.5f+i*1.6f);
                coin.transform.localScale=Vector3.one*1.7f;
                coin.AddComponent<MeshFilter>().sharedMesh=coinMesh;
                coin.AddComponent<MeshRenderer>().sharedMaterials=new Material[]{coinBodyMat,coinStarMat};
                SphereCollider col=coin.AddComponent<SphereCollider>(); col.isTrigger=true; col.radius=.6f;
                Rigidbody rb=coin.AddComponent<Rigidbody>(); rb.isKinematic=true; rb.useGravity=false;
                coin.AddComponent<NeonVelocityCoin>().manager=this;
                activeWorldObjects.Add(coin);
            }
        }

        // Buildings: simplified so the skyline reads clean instead of cluttered.
        for(int side=-1;side<=1;side+=2)
        {
            float height=Random.Range(10f,28f), width=Random.Range(3f,5f);
            float x=side*Random.Range(8f,12f);
            GameObject building=MakeCube("Neon City Building",new Vector3(x,height/2f,spawnZ),new Vector3(width,height,chunkLength),buildingMat);
            Destroy(building.GetComponent<Collider>()); activeWorldObjects.Add(building);

            Material stripeMat=NeonAt(chunkIndex+(side>0?1:0));
            GameObject stripe=MakeCube("Building Neon Strip",new Vector3(x-side*(width*.5f+.03f),height/2f,spawnZ-chunkLength*.45f),new Vector3(.08f,height*.9f,.08f),stripeMat);
            Destroy(stripe.GetComponent<Collider>()); activeWorldObjects.Add(stripe);

            GameObject roofLine=MakeCube("Building Roof Line",new Vector3(x-side*(width*.5f+.03f),height,spawnZ),new Vector3(.08f,.1f,chunkLength*.95f),stripeMat);
            Destroy(roofLine.GetComponent<Collider>()); activeWorldObjects.Add(roofLine);

            for(int k=0;k<4;k++)
            {
                GameObject window=MakeCube("Building Window",new Vector3(x-side*(width*.5f+.03f),Random.Range(2f,height-1.5f),spawnZ+Random.Range(-6f,6f)),new Vector3(.05f,.5f,.8f),RandomWindow());
                Destroy(window.GetComponent<Collider>()); activeWorldObjects.Add(window);
            }
        }

        for(int side=-1;side<=1;side+=2)
        {
            Material lamp=NeonAt(chunkIndex*2+(side>0?1:0));
            GameObject post=MakeCube("Neon Lamp",new Vector3(side*5.2f,1.4f,spawnZ),new Vector3(.1f,2.8f,.1f),lamp);
            Destroy(post.GetComponent<Collider>()); activeWorldObjects.Add(post);
            GameObject cap=MakeCube("Neon Lamp Top",new Vector3(side*4.9f,2.85f,spawnZ),new Vector3(.7f,.1f,.1f),lamp);
            Destroy(cap.GetComponent<Collider>()); activeWorldObjects.Add(cap);
        }

        if(spawnZ>25f && chunkIndex%4==0)
        {
            Material arch=NeonAt(chunkIndex/4);
            for(int side=-1;side<=1;side+=2)
            {
                GameObject post=MakeCube("Neon Arch Post",new Vector3(side*4.6f,3.2f,spawnZ+5f),new Vector3(.2f,6.4f,.2f),arch);
                Destroy(post.GetComponent<Collider>()); activeWorldObjects.Add(post);
            }
            GameObject top=MakeCube("Neon Arch Top",new Vector3(0,6.4f,spawnZ+5f),new Vector3(9.45f,.2f,.2f),arch);
            Destroy(top.GetComponent<Collider>()); activeWorldObjects.Add(top);
        }
        chunkIndex++; spawnZ+=chunkLength;
    }

    void CleanupOldObjects()
    {
        float limit=player.transform.position.z-40f;
        for(int i=activeWorldObjects.Count-1;i>=0;i--)
        {
            GameObject obj=activeWorldObjects[i];
            if(obj==null) { activeWorldObjects.RemoveAt(i); continue; }
            if(obj.transform.position.z<limit)
            {
                Destroy(obj); activeWorldObjects.RemoveAt(i);
            }
        }
    }

    GameObject Part(string n,PrimitiveType type,Transform parent,Vector3 pos,Vector3 scale,Material mat)
    {
        GameObject g=GameObject.CreatePrimitive(type); g.name=n;
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent,false); g.transform.localPosition=pos; g.transform.localScale=scale;
        g.GetComponent<Renderer>().material=mat; return g;
    }

    Transform Pivot(string n,Transform parent,Vector3 pos)
    {
        GameObject g=new GameObject(n); g.transform.SetParent(parent,false); g.transform.localPosition=pos;
        return g.transform;
    }

    Light AddLight(Transform parent,string n,Vector3 pos,Color col,float range,float intensity)
    {
        GameObject o=new GameObject(n);
        o.transform.SetParent(parent,false); o.transform.localPosition=pos;
        Light l=o.AddComponent<Light>(); l.type=LightType.Point;
        l.color=col; l.range=range; l.intensity=intensity;
        return l;
    }

    void CreatePlayer()
    {
        GameObject root=new GameObject("Player");
        root.transform.position=new Vector3(0,1f,0);
        player=root.AddComponent<CharacterController>();
        player.height=2f; player.radius=.5f; player.center=Vector3.zero; player.stepOffset=.3f;
        model=Pivot("Runner Model",root.transform,new Vector3(0,-1f,0));

        // Softer, moodier lights that follow the runner: cool key, cyan fill, magenta rim.
        AddLight(root.transform,"Runner Key",new Vector3(0,3.2f,-3f),new Color(1f,.85f,.7f),12f,4.5f);
        AddLight(root.transform,"Runner Fill",new Vector3(-2f,1.5f,-1f),new Color(.5f,.6f,1f),9f,1.8f);
        AddLight(root.transform,"Runner Rim",new Vector3(1.5f,2.2f,2f),new Color(.7f,.35f,1f),9f,2.2f);


        if(characterPrefab!=null)
        {
            GameObject inst=Instantiate(characterPrefab,model);
            inst.transform.localPosition=new Vector3(0,modelYOffset,0);
            inst.transform.localRotation=Quaternion.identity;
            inst.transform.localScale=Vector3.one*modelScale;
            foreach(Collider c in inst.GetComponentsInChildren<Collider>()) Destroy(c);
            Animator anim=inst.GetComponentInChildren<Animator>();
            if(anim==null) anim=inst.AddComponent<Animator>();
            anim.applyRootMotion=false; SetupAnimator(anim); usingPrefab=true; return;
        }

        model.localScale=Vector3.one*1.6f;

        // ---- Cartoon boy: polo shirt, jeans, big head, glasses, spiky brown hair, big ears
        Part("Shirt",PrimitiveType.Capsule,model,new Vector3(0,1.22f,0),new Vector3(.5f,.32f,.3f),shirtMat);
        Part("Collar",PrimitiveType.Cube,model,new Vector3(0,1.53f,.02f),new Vector3(.3f,.06f,.2f),detailMat);
        Part("Hips",PrimitiveType.Cube,model,new Vector3(0,.86f,0),new Vector3(.46f,.2f,.3f),pantsMat);

        Part("Head",PrimitiveType.Sphere,model,new Vector3(0,1.86f,0),Vector3.one*.56f,skinMat);
        Part("Ear L",PrimitiveType.Sphere,model,new Vector3(-.29f,1.84f,0),new Vector3(.1f,.15f,.07f),skinMat);
        Part("Ear R",PrimitiveType.Sphere,model,new Vector3(.29f,1.84f,0),new Vector3(.1f,.15f,.07f),skinMat);
        Part("Hair",PrimitiveType.Sphere,model,new Vector3(0,1.93f,-.05f),new Vector3(.6f,.5f,.58f),hairMat);
        Part("Tuft 1",PrimitiveType.Cube,model,new Vector3(0,2.2f,.02f),new Vector3(.14f,.2f,.14f),hairMat).transform.localRotation=Quaternion.Euler(10f,0,0);
        Part("Tuft 2",PrimitiveType.Cube,model,new Vector3(-.14f,2.15f,0),new Vector3(.14f,.2f,.14f),hairMat).transform.localRotation=Quaternion.Euler(0,0,25f);
        Part("Tuft 3",PrimitiveType.Cube,model,new Vector3(.14f,2.15f,0),new Vector3(.14f,.2f,.14f),hairMat).transform.localRotation=Quaternion.Euler(0,0,-25f);
        Part("Tuft 4",PrimitiveType.Cube,model,new Vector3(-.07f,2.2f,-.1f),new Vector3(.13f,.2f,.13f),hairMat).transform.localRotation=Quaternion.Euler(-15f,0,15f);
        Part("Tuft 5",PrimitiveType.Cube,model,new Vector3(.08f,2.2f,-.1f),new Vector3(.13f,.2f,.13f),hairMat).transform.localRotation=Quaternion.Euler(-15f,0,-15f);

        // face: glasses, big eyes, nose, smile
        for(int i=-1;i<=1;i+=2)
        {
            Part("Glass Rim",PrimitiveType.Cylinder,model,new Vector3(i*.1f,1.88f,.262f),new Vector3(.19f,.008f,.19f),darkMat).transform.localRotation=Quaternion.Euler(90f,0,0);
            Part("Eye",PrimitiveType.Sphere,model,new Vector3(i*.1f,1.88f,.255f),Vector3.one*.12f,eyeMat);
            Part("Pupil",PrimitiveType.Sphere,model,new Vector3(i*.1f,1.88f,.305f),Vector3.one*.055f,darkMat);
            Part("Glass Arm",PrimitiveType.Cube,model,new Vector3(i*.19f,1.88f,.12f),new Vector3(.015f,.015f,.26f),darkMat);
        }
        Part("Glass Bridge",PrimitiveType.Cube,model,new Vector3(0,1.89f,.275f),new Vector3(.06f,.015f,.015f),darkMat);
        Part("Nose",PrimitiveType.Sphere,model,new Vector3(0,1.8f,.285f),Vector3.one*.07f,skinMat);
        Part("Mouth",PrimitiveType.Cube,model,new Vector3(0,1.72f,.255f),new Vector3(.1f,.02f,.02f),darkMat);

        leftArm=BuildArm("Left Arm",-.33f); rightArm=BuildArm("Right Arm",.33f);
        leftLeg=BuildLeg("Left Leg",-.14f); rightLeg=BuildLeg("Right Leg",.14f);
        leftShin=leftLeg.Find("Shin"); rightShin=rightLeg.Find("Shin");
    }

    Transform BuildArm(string n,float x)
    {
        Transform p=Pivot(n,model,new Vector3(x,1.42f,0));
        Part("Sleeve",PrimitiveType.Capsule,p,new Vector3(0,-.1f,0),new Vector3(.17f,.13f,.17f),sleeveMat);
        Transform fore=Pivot("Forearm",p,new Vector3(0,-.28f,0));
        fore.localRotation=Quaternion.Euler(-95f,0,0);
        Part("Forearm Skin",PrimitiveType.Capsule,fore,new Vector3(0,-.13f,0),new Vector3(.1f,.14f,.1f),skinMat);
        Part("Hand",PrimitiveType.Sphere,fore,new Vector3(0,-.3f,0),Vector3.one*.15f,skinMat);
        return p;
    }

    Transform BuildLeg(string n,float x)
    {
        Transform p=Pivot(n,model,new Vector3(x,.82f,0));
        Part("Thigh",PrimitiveType.Capsule,p,new Vector3(0,-.2f,0),new Vector3(.24f,.23f,.26f),pantsMat);
        Transform shin=Pivot("Shin",p,new Vector3(0,-.4f,0));
        Part("Shin Pants",PrimitiveType.Capsule,shin,new Vector3(0,-.2f,0),new Vector3(.2f,.23f,.22f),pantsMat);
        Part("Pants Cuff",PrimitiveType.Cube,shin,new Vector3(0,-.33f,0),new Vector3(.23f,.06f,.23f),pantsMat);
        Part("Shoe",PrimitiveType.Sphere,shin,new Vector3(0,-.34f,.06f),new Vector3(.25f,.14f,.38f),shoeMat);
        return p;
    }

    void SetupAnimator(Animator anim)
    {
        if(runClip==null) { Debug.LogWarning("Assign a Run Clip to use Mixamo animation."); return; }
        graph=PlayableGraph.Create("Runner Animation");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        AnimationPlayableOutput output=AnimationPlayableOutput.Create(graph,"Animation",anim);
        mixer=AnimationMixerPlayable.Create(graph,jumpClip!=null?2:1);
        runPlayable=AnimationClipPlayable.Create(graph,runClip); runPlayable.SetSpeed(0);
        graph.Connect(runPlayable,0,mixer,0); mixer.SetInputWeight(0,1f);
        if(jumpClip!=null)
        {
            jumpPlayable=AnimationClipPlayable.Create(graph,jumpClip); jumpPlayable.SetSpeed(0);
            graph.Connect(jumpPlayable,0,mixer,1); mixer.SetInputWeight(1,0f);
        }
        output.SetSourcePlayable(mixer); graph.Play(); graphReady=true;
    }

    void UpdateClipAnimation()
    {
        if(!graphReady) return;
        runClock+=Time.deltaTime*runClipSpeed*(currentSpeed/forwardSpeed);
        runPlayable.SetTime(runClock%Mathf.Max(.01f,runClip.length));
        int clipStep=Mathf.FloorToInt(runClock/Mathf.Max(.01f,runClip.length)*2f);
        if(onGround && clipStep!=lastStepIndex) { lastStepIndex=clipStep; Footstep(); }
        if(jumpClip!=null)
        {
            if(onGround)
            {
                jumpClock=0; mixer.SetInputWeight(0,1); mixer.SetInputWeight(1,0);
            }
            else
            {
                jumpClock+=Time.deltaTime*Mathf.Max(1f,jumpClip.length/.7f);
                jumpPlayable.SetTime(Mathf.Min(jumpClock,Mathf.Max(0,jumpClip.length-.02f)));
                mixer.SetInputWeight(0,0); mixer.SetInputWeight(1,1);
            }
        }
    }

    void AnimateRunner(float xDiff)
    {
        if(model==null) return;
        float twist=0f;
        if(usingPrefab) UpdateClipAnimation();
        else if(onGround)
        {
            // Stride speeds up as the game speeds up -> reads as a real sprint, not a walk.
            runPhase+=Time.deltaTime*runAnimSpeed*(currentSpeed/forwardSpeed);
            // a footstep every time a foot hits the ground (twice per stride)
            int stepIdx=Mathf.FloorToInt((runPhase-Mathf.PI*.5f)/Mathf.PI);
            if(stepIdx!=lastStepIndex) { lastStepIndex=stepIdx; Footstep(); }
            float t=runPhase, s=Mathf.Sin(t), c=Mathf.Cos(t);
            float swing=s*runSwing;

            leftLeg.localRotation=Quaternion.Euler(swing,0,0);
            rightLeg.localRotation=Quaternion.Euler(-swing,0,0);
            // Heels kick up high behind the body
            leftShin.localRotation=Quaternion.Euler(15f+Mathf.Max(0,-c)*kneeBend,0,0);
            rightShin.localRotation=Quaternion.Euler(15f+Mathf.Max(0,c)*kneeBend,0,0);
            // Big pumping arms
            leftArm.localRotation=Quaternion.Euler(-swing*1.15f,0,0);
            rightArm.localRotation=Quaternion.Euler(swing*1.15f,0,0);
            // Bouncy stride
            model.localPosition=new Vector3(0,-1f+Mathf.Abs(c)*.14f,0);
            twist=s*7f;
            if(ponytail!=null) ponytail.localRotation=Quaternion.Euler(40f+Mathf.Sin(t+1f)*25f,Mathf.Sin(t*.5f)*16f,0);
        }
        else if(!usingPrefab)
        {
            leftLeg.localRotation=Quaternion.Euler(-55f,0,0);
            rightLeg.localRotation=Quaternion.Euler(25f,0,0);
            leftShin.localRotation=Quaternion.Euler(75f,0,0);
            rightShin.localRotation=Quaternion.Euler(35f,0,0);
            leftArm.localRotation=Quaternion.Euler(-150f,0,0);
            rightArm.localRotation=Quaternion.Euler(-150f,0,0);
            model.localPosition=new Vector3(0,-1f,0);
            if(ponytail!=null) ponytail.localRotation=Quaternion.Euler(55f,0,0);
        }
        float lean=Mathf.Clamp(-xDiff*8f,-18f,18f);
        model.localRotation=Quaternion.Slerp(model.localRotation,Quaternion.Euler(usingPrefab?0f:forwardLean,twist,lean),14f*Time.deltaTime);
    }

    void Footstep()
    {
        if(player==null) return;
        Vector3 fp=player.transform.position+new Vector3(Random.Range(-.15f,.15f),-.93f,-.1f);
        for(int i=0;i<3;i++)
        {
            GameObject dust=GameObject.CreatePrimitive(PrimitiveType.Cube);
            dust.name="Dust"; Destroy(dust.GetComponent<Collider>());
            dust.transform.position=fp; dust.transform.localScale=Vector3.one*Random.Range(.08f,.16f);
            dust.GetComponent<Renderer>().sharedMaterial=dustMat;
            NeonVelocityBurst b=dust.AddComponent<NeonVelocityBurst>();
            b.life=.45f;
            b.velocity=new Vector3(Random.Range(-.8f,.8f),Random.Range(.8f,1.8f),Random.Range(-.6f,.2f));
        }
    }

    void ApplySkin(int index)
    {
        skinIndex=(index%skins.Length+skins.Length)%skins.Length;
        SkinPreset p=skins[skinIndex];
        SetMatColor(skinMat,p.skin,.5f);
        SetMatColor(hairMat,p.hair,.45f);
        SetMatColor(shirtMat,p.shirt,.55f);
        SetMatColor(pantsMat,p.pants,.5f);
        SetMatColor(shoeMat,p.shoes,.5f);
        SetMatColor(accentMat,p.accent,.5f);
        SetMatColor(sleeveMat,p.sleeve,.5f);
        SetMatColor(detailMat,p.detail,.5f);
        PlayerPrefs.SetInt("NV_Skin3",skinIndex);
        if(skinButtonText!=null) skinButtonText.text="SKIN\n"+p.name;
    }

    public void CycleSkin() => ApplySkin(skinIndex+1);

    void CreateCamera()
    {
        gameCamera=Camera.main;
        if(gameCamera==null)
        {
            GameObject cam=new GameObject("Main Camera"); cam.tag="MainCamera";
            gameCamera=cam.AddComponent<Camera>();
        }
        gameCamera.transform.position=new Vector3(0,4.5f,-7.5f);
        gameCamera.fieldOfView=65f;
        gameCamera.allowHDR=true;
        gameCamera.clearFlags=CameraClearFlags.SolidColor;
        gameCamera.backgroundColor=new Color(.005f,.003f,.02f);
    }

    void SetupGlow()
    {
        if(!enableBloom) return;
        Volume vol=FindFirstObjectByType<Volume>();
        if(vol==null)
        {
            GameObject g=new GameObject("Neon Volume");
            vol=g.AddComponent<Volume>(); vol.isGlobal=true;
            vol.profile=ScriptableObject.CreateInstance<VolumeProfile>();
        }
        vol.weight=1f;
        Bloom bloom;
        if(!vol.profile.TryGet(out bloom)) bloom=vol.profile.Add<Bloom>(true);
        bloom.active=true;
        // Stronger bloom so neon edges pop against the dark scene.
        bloom.intensity.Override(.6f);
        bloom.threshold.Override(1.4f);
        bloom.scatter.Override(.45f);
        bloom.tint.Override(new Color(.55f,.5f,1f));
        Vignette vig; if(!vol.profile.TryGet(out vig)) vig=vol.profile.Add<Vignette>(true);
        vig.active=true; vig.intensity.Override(.3f);
        ColorAdjustments ca; if(!vol.profile.TryGet(out ca)) ca=vol.profile.Add<ColorAdjustments>(true);
        ca.active=true; ca.postExposure.Override(-.2f);
        UniversalAdditionalCameraData data=gameCamera.GetUniversalAdditionalCameraData();
        if(data!=null) data.renderPostProcessing=true;
    }

    void CreateUI()
    {
        GameObject canvasObject=new GameObject("Game Canvas");
        Canvas canvas=canvasObject.AddComponent<Canvas>();
        canvas.renderMode=RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder=100;
        CanvasScaler scaler=canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution=new Vector2(1920,1080);
        scaler.matchWidthOrHeight=.5f;
        canvasObject.AddComponent<GraphicRaycaster>();

        if(FindFirstObjectByType<EventSystem>()==null)
        {
            GameObject es=new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
        Transform c=canvasObject.transform;

        // Live score: top-center panel anchored to the top edge so it always stays on screen
        GameObject sp=new GameObject("Score Panel");
        sp.transform.SetParent(c,false);
        RectTransform spr=sp.AddComponent<RectTransform>();
        spr.anchorMin=new Vector2(.5f,1); spr.anchorMax=new Vector2(.5f,1); spr.pivot=new Vector2(.5f,1);
        spr.anchoredPosition=new Vector2(0,-20); spr.sizeDelta=new Vector2(420,120);
        Image spBg=sp.AddComponent<Image>(); spBg.color=new Color(.12f,.04f,.25f,.85f); spBg.raycastTarget=false;
        Outline spOut=sp.AddComponent<Outline>(); spOut.effectColor=new Color(1f,.3f,.85f,1f); spOut.effectDistance=new Vector2(3,3);
        scoreOutline=spOut;
        Glow(CreateStretchText(sp.transform,"SCORE",.64f,.96f,26,new Color(1f,.8f,.25f)),new Color(.5f,.2f,0f,.9f));
        scoreText=Glow(CreateStretchText(sp.transform,"000000",.04f,.68f,70,new Color(.4f,1f,1f)),new Color(0f,.35f,.8f,.95f));
        scorePopupText=Glow(CreateLabel(c,"",new Vector2(.5f,1),new Vector2(0,-150),46,TextAnchor.MiddleCenter),new Color(1f,.4f,0f,.95f));
        scorePopupText.color=new Color(1f,.95f,.3f,0f);

        Transform infoPanel=CreateHudPanel(c,"Info Panel",new Vector2(1,1),new Vector2(-175,-100),new Vector2(330,140));
        infoOutline=infoPanel.GetComponent<Outline>();
        coinText=Glow(CreateHudText(infoPanel,"COINS  0",new Vector2(0,28),32,new Color(1f,.92f,.25f)),new Color(.9f,.35f,0f,.95f));
        distanceText=Glow(CreateHudText(infoPanel,"RUN  0 m",new Vector2(0,-27),26,new Color(.55f,1f,.45f)),new Color(0f,.4f,.1f,.95f));

        messageText=CreateLabel(c,"",new Vector2(.5f,.5f),Vector2.zero,48,TextAnchor.MiddleCenter);
        Button pauseButton=CreateButton(c,"II",new Vector2(0,1),new Vector2(80,-80),new Vector2(100,82),TogglePause);
        pauseText=pauseButton.GetComponentInChildren<Text>();
        CreateButton(c,"RESTART",new Vector2(0,1),new Vector2(205,-80),new Vector2(175,82),RestartGame);

        if(showMoveButtons)
        {
            CreateButton(c,"LEFT",new Vector2(0,0),new Vector2(100,100),new Vector2(150,90),()=>ChangeLane(-1));
            CreateButton(c,"RIGHT",new Vector2(0,0),new Vector2(270,100),new Vector2(150,90),()=>ChangeLane(1));
        }
        CreateButton(c,"JUMP",new Vector2(1,0),new Vector2(-120,100),new Vector2(170,100),Jump);
        Button skinBtn=CreateButton(c,"SKIN",new Vector2(.5f,0),new Vector2(0,100),new Vector2(170,90),CycleSkin);
        skinButtonText=skinBtn.GetComponentInChildren<Text>();
        skinBtn.gameObject.SetActive(showSkinButton);

        gameOverPanel=new GameObject("Game Over Panel");
        gameOverPanel.transform.SetParent(c,false);
        RectTransform pr=gameOverPanel.AddComponent<RectTransform>();
        pr.anchorMin=Vector2.zero; pr.anchorMax=Vector2.one; pr.offsetMin=Vector2.zero; pr.offsetMax=Vector2.zero;
        Image bg=gameOverPanel.AddComponent<Image>(); bg.color=new Color(.03f,.01f,.08f,.92f);
        Text title=CreateLabel(gameOverPanel.transform,"GAME OVER",new Vector2(.5f,.5f),new Vector2(0,150),90,TextAnchor.MiddleCenter);
        title.color=new Color(1f,.15f,.6f);
        goScoreText=CreateLabel(gameOverPanel.transform,"SCORE  0",new Vector2(.5f,.5f),new Vector2(0,40),56,TextAnchor.MiddleCenter);
        goScoreText.color=new Color(0f,.95f,1f);
        CreateButton(gameOverPanel.transform,"PLAY AGAIN",new Vector2(.5f,.5f),new Vector2(0,-100),new Vector2(340,110),RestartGame);
        gameOverPanel.SetActive(false);
    }

    Transform CreateHudPanel(Transform parent,string panelName,Vector2 anchor,Vector2 position,Vector2 size)
    {
        GameObject panel=new GameObject(panelName);
        panel.transform.SetParent(parent,false);
        RectTransform rect=panel.AddComponent<RectTransform>();
        rect.anchorMin=anchor; rect.anchorMax=anchor;
        rect.pivot=new Vector2(.5f,.5f);
        rect.anchoredPosition=position; rect.sizeDelta=size;
        Image bg=panel.AddComponent<Image>();
        bg.color=new Color(.12f,.04f,.25f,.85f);
        Outline outline=panel.AddComponent<Outline>();
        outline.effectColor=new Color(1f,.3f,.85f,1f);
        outline.effectDistance=new Vector2(3,3);
        return panel.transform;
    }

    Text Glow(Text t,Color outline)
    {
        Outline o=t.GetComponent<Outline>();
        if(o!=null) { o.effectColor=outline; o.effectDistance=new Vector2(3,-3); }
        return t;
    }

    Text CreateStretchText(Transform parent,string content,float yMin,float yMax,int maxSize,Color color)
    {
        GameObject obj=new GameObject("Score Text");
        obj.transform.SetParent(parent,false);
        RectTransform rect=obj.AddComponent<RectTransform>();
        rect.anchorMin=new Vector2(0,yMin); rect.anchorMax=new Vector2(1,yMax);
        rect.offsetMin=new Vector2(8,0); rect.offsetMax=new Vector2(-8,0);
        Text txt=obj.AddComponent<Text>();
        txt.text=content; txt.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontStyle=FontStyle.Bold; txt.alignment=TextAnchor.MiddleCenter; txt.color=color;
        txt.resizeTextForBestFit=true; txt.resizeTextMinSize=12; txt.resizeTextMaxSize=maxSize;
        txt.raycastTarget=false;
        Outline o=obj.AddComponent<Outline>(); o.effectColor=Color.black; o.effectDistance=new Vector2(2,-2);
        return txt;
    }

    Text CreateHudText(Transform parent,string content,Vector2 position,int fontSize,Color color)
    {
        GameObject obj=new GameObject("HUD Text");
        obj.transform.SetParent(parent,false);
        RectTransform rect=obj.AddComponent<RectTransform>();
        rect.anchorMin=new Vector2(.5f,.5f); rect.anchorMax=new Vector2(.5f,.5f);
        rect.pivot=new Vector2(.5f,.5f); rect.anchoredPosition=position;
        rect.sizeDelta=new Vector2(450,62);
        Text txt=obj.AddComponent<Text>();
        txt.text=content; txt.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize=fontSize; txt.fontStyle=FontStyle.Bold;
        txt.alignment=TextAnchor.MiddleCenter; txt.color=color;
        txt.raycastTarget=false; txt.horizontalOverflow=HorizontalWrapMode.Overflow;
        txt.verticalOverflow=VerticalWrapMode.Overflow;
        Outline shadow=obj.AddComponent<Outline>();
        shadow.effectColor=Color.black; shadow.effectDistance=new Vector2(2,-2);
        return txt;
    }

    Text CreateLabel(Transform parent,string content,Vector2 anchor,Vector2 position,int fontSize,TextAnchor alignment)
    {
        GameObject obj=new GameObject("UI Text"); obj.transform.SetParent(parent,false);
        RectTransform rect=obj.AddComponent<RectTransform>();
        rect.anchorMin=anchor; rect.anchorMax=anchor; rect.pivot=anchor;
        rect.anchoredPosition=position; rect.sizeDelta=new Vector2(700,100);
        Text txt=obj.AddComponent<Text>();
        txt.text=content; txt.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize=fontSize; txt.alignment=alignment; txt.color=Color.white;
        txt.fontStyle=FontStyle.Bold; txt.horizontalOverflow=HorizontalWrapMode.Overflow;
        txt.verticalOverflow=VerticalWrapMode.Overflow; txt.raycastTarget=false;
        Outline outline=obj.AddComponent<Outline>();
        outline.effectColor=new Color(0,0,0,.95f); outline.effectDistance=new Vector2(2.5f,-2.5f);
        return txt;
    }

    Button CreateButton(Transform parent,string caption,Vector2 anchor,Vector2 position,Vector2 size,UnityEngine.Events.UnityAction action)
    {
        GameObject obj=new GameObject(caption+" Button"); obj.transform.SetParent(parent,false);
        RectTransform rect=obj.AddComponent<RectTransform>();
        rect.anchorMin=anchor; rect.anchorMax=anchor; rect.pivot=anchor;
        rect.anchoredPosition=position; rect.sizeDelta=size;
        Image image=obj.AddComponent<Image>(); image.color=new Color(.035f,.012f,.10f,.94f);
        Button button=obj.AddComponent<Button>(); button.targetGraphic=image; button.onClick.AddListener(action);
        ColorBlock colors=button.colors;
        colors.normalColor=new Color(.12f,.035f,.22f,1);
        colors.highlightedColor=new Color(.2f,.1f,.4f,1);
        colors.pressedColor=new Color(.0f,.45f,.6f,1);
        button.colors=colors;
        Outline border=obj.AddComponent<Outline>();
        border.effectColor=new Color(0f,.65f,.95f,.9f); border.effectDistance=new Vector2(2,2);

        GameObject label=new GameObject("Label"); label.transform.SetParent(obj.transform,false);
        RectTransform lr=label.AddComponent<RectTransform>();
        lr.anchorMin=Vector2.zero; lr.anchorMax=Vector2.one;
        lr.offsetMin=Vector2.zero; lr.offsetMax=Vector2.zero;
        Text txt=label.AddComponent<Text>();
        txt.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.text=caption; txt.fontSize=25; txt.fontStyle=FontStyle.Bold;
        txt.alignment=TextAnchor.MiddleCenter; txt.color=Color.white; txt.raycastTarget=false;
        return button;
    }

    void RefreshHud()
    {
        if(scoreText!=null) scoreText.text=Score.ToString("D6");
        if(coinText!=null) coinText.text="COINS  "+coins;
        if(distanceText!=null) distanceText.text="RUN  "+Mathf.Max(0,Mathf.FloorToInt(distance))+" m";
    }

    public void ChangeLane(int direction)
    {
        if(paused||gameOver) return;
        lane=Mathf.Clamp(lane+direction,0,2);
    }

    public void Jump()
    {
        if(paused||gameOver||player==null) return;
        if(onGround||player.isGrounded) { verticalSpeed=jumpVelocity; jumpBuffer=0f; }
        else jumpBuffer=.15f;
    }

    void UpdateHudFx()
    {
        if(scoreText!=null)
        {
            scorePulse=Mathf.MoveTowards(scorePulse,0f,Time.deltaTime*3.5f);
            scoreText.transform.localScale=Vector3.one*(1f+scorePulse*.15f);
            float hue=Time.time*.2f;
            scoreText.color=Color.Lerp(Color.HSVToRGB(Mathf.Repeat(hue,1f),.55f,1f),Color.white,scorePulse);
            if(scoreOutline!=null) scoreOutline.effectColor=Color.HSVToRGB(Mathf.Repeat(hue+.5f,1f),.8f,1f);
            if(infoOutline!=null) infoOutline.effectColor=Color.HSVToRGB(Mathf.Repeat(hue+.25f,1f),.8f,1f);
        }
        if(scorePopupText!=null)
        {
            popupTimer=Mathf.Max(0f,popupTimer-Time.deltaTime);
            float a=Mathf.Clamp01(popupTimer/.9f);
            scorePopupText.color=new Color(1f,.95f,.3f,a);
            scorePopupText.rectTransform.anchoredPosition=new Vector2(0,-150f+(1f-a)*30f);
        }
    }

    public void SpawnBurst(Vector3 pos)
    {
        for(int i=0;i<10;i++)
        {
            GameObject sp=GameObject.CreatePrimitive(PrimitiveType.Cube);
            sp.name="Coin Spark"; Destroy(sp.GetComponent<Collider>());
            sp.transform.position=pos; sp.transform.localScale=Vector3.one*Random.Range(.08f,.16f);
            sp.GetComponent<Renderer>().sharedMaterial=goldMat;
            NeonVelocityBurst b=sp.AddComponent<NeonVelocityBurst>();
            b.velocity=Random.onUnitSphere*Random.Range(2.5f,5f)+new Vector3(0,1.5f,currentSpeed*.6f);
        }
    }

    public void CollectCoin()
    {
        if(gameOver) return;
        coins++; scorePulse=1f; popupTimer=.9f;
        if(scorePopupText!=null) scorePopupText.text="+"+coinScoreValue;
        RefreshHud();
    }

    void TogglePause()
    {
        if(gameOver) return;
        paused=!paused; Time.timeScale=paused?0f:1f;
        if(pauseText!=null) pauseText.text=paused?"PLAY":"II";
    }

    public void EndGame()
    {
        if(gameOver) return;
        gameOver=true;
        if(goScoreText!=null) goScoreText.text="SCORE  "+Score;
        if(gameOverPanel!=null) gameOverPanel.SetActive(true);
    }

    public void RestartGame()
    {
        Time.timeScale=1f; paused=false; gameOver=false;
        coins=0; distance=0; lane=1; verticalSpeed=0; currentSpeed=forwardSpeed; runPhase=0; lastStepIndex=-1;
        spawnZ=10f; chunkIndex=0;
        foreach(GameObject obj in activeWorldObjects) if(obj!=null) Destroy(obj);
        activeWorldObjects.Clear();

        if(player!=null)
        {
            player.enabled=false;
            player.transform.position=new Vector3(0,1f,0);
            player.enabled=true;
        }
        popupTimer=0f; scorePulse=0f;
        if(scorePopupText!=null) scorePopupText.text="";
        for(int i=0;i<15;i++) SpawnChunk();
        if(gameCamera!=null) gameCamera.transform.position=player.transform.position+new Vector3(0,4.5f,-7.5f);
        if(gameOverPanel!=null) gameOverPanel.SetActive(false);
        if(pauseText!=null) pauseText.text="II";
        if(messageText!=null) messageText.text="";
        RefreshHud();
    }
}

public class NeonVelocityObstacle : MonoBehaviour
{
    public NeonVelocityStarter manager;
    void OnTriggerEnter(Collider other)
    {
        if(other.GetComponent<CharacterController>()!=null && manager!=null) manager.EndGame();
    }
}

public class NeonVelocityCoin : MonoBehaviour
{
    public NeonVelocityStarter manager;
    float baseY, phase;
    bool pulled, taken;
    void Start()
    {
        baseY=transform.position.y;
        phase=Random.value*6.28f;
        transform.rotation=Quaternion.Euler(0,Random.Range(0f,360f),0);
    }
    void Update()
    {
        transform.Rotate(0,260f*Time.deltaTime,0,Space.World);
        if(manager!=null)
        {
            // Magnet: coins close to the runner fly into them.
            Vector3 pp=manager.PlayerPosition;
            float r=manager.coinMagnetRadius;
            if(!pulled && (transform.position-pp).sqrMagnitude<r*r) pulled=true;
            if(pulled)
            {
                transform.position=Vector3.MoveTowards(transform.position,pp,(manager.CurrentSpeed+16f)*Time.deltaTime);
                return;
            }
        }
        Vector3 p=transform.position;
        p.y=baseY+Mathf.Sin(Time.time*3f+phase)*.12f;
        transform.position=p;
    }
    void OnTriggerEnter(Collider other)
    {
        if(taken) return;
        if(other.GetComponent<CharacterController>()!=null)
        {
            taken=true;
            if(manager!=null) { manager.SpawnBurst(transform.position); manager.CollectCoin(); }
            Destroy(gameObject);
        }
    }
}

public class NeonVelocityBurst : MonoBehaviour
{
    public Vector3 velocity;
    float t;
    public float life=.5f;
    Vector3 startScale;
    void Start() { startScale=transform.localScale; }
    void Update()
    {
        t+=Time.deltaTime;
        velocity+=Vector3.down*6f*Time.deltaTime;
        transform.position+=velocity*Time.deltaTime;
        transform.Rotate(300f*Time.deltaTime,200f*Time.deltaTime,0f);
        transform.localScale=startScale*Mathf.Max(0f,1f-t/life);
        if(t>=life) Destroy(gameObject);
    }
}