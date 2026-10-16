using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class SoloCameraTestManager : MonoBehaviour
{
    public enum CameraViewMode
    {
        MirrorFront = 1,   // Direct front view (like a boxing gym mirror)
        SideProfile = 2,   // Side view (inspect forward punch depth & arm extension)
        ThirdPerson = 3,   // Over-the-shoulder view
        FreeOrbit = 4      // Mouse orbit view
    }

    [Header("Camera Perspectives")]
    [Tooltip("Active camera viewpoint. Press keys 1, 2, 3, 4 or click on-screen buttons.")]
    public CameraViewMode currentView = CameraViewMode.MirrorFront;
    public float cameraDistance = 2.6f;
    public float cameraHeight = 1.35f;

    [Header("Interactive Target")]
    [Tooltip("Optional heavy punching bag. Disabled by default so camera view is completely clear.")]
    public bool spawnHeavyBag = false;

    private Camera targetCamera;
    private AvatarController avatar;
    private Transform playerTransform;

    // Orbit camera controls
    private float orbitYaw = 180f;
    private float orbitPitch = 10f;
    private Vector3 initialPlayerPos;

    // Punching bag references
    private GameObject heavyBag;
    private Rigidbody bagRb;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttachToSoloScene()
    {
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (sceneName.Contains("Solo") || sceneName.Contains("CameraTest") || sceneName.Contains("SampleScene"))
        {
            if (Object.FindAnyObjectByType<SoloCameraTestManager>() == null)
            {
                GameObject go = new GameObject("SoloCameraTestRig");
                go.AddComponent<SoloCameraTestManager>();
                Debug.Log($"[SoloCameraTestManager] Auto-attached to '{sceneName}' for solo camera testing.");
            }
        }
    }

    void Start()
    {
        // Unlock cursor so user can click on-screen perspective buttons
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // 1. Locate player avatar
        avatar = FindAnyObjectByType<AvatarController>();
        if (avatar != null)
        {
            playerTransform = avatar.transform;
            initialPlayerPos = playerTransform.position;
        }
        else
        {
            GameObject playerGo = GameObject.Find("PlayerArmature");
            if (playerGo != null)
            {
                playerTransform = playerGo.transform;
                initialPlayerPos = playerTransform.position;
            }
        }

        if (playerTransform != null)
        {
            if (avatar == null) avatar = playerTransform.GetComponent<AvatarController>();
            var tpc = playerTransform.GetComponent("ThirdPersonController") as MonoBehaviour;
            if (tpc != null) tpc.enabled = false;
        }

        // 2. Setup camera
        targetCamera = Camera.main;
        if (targetCamera == null)
        {
            GameObject camGo = new GameObject("TestCamera");
            targetCamera = camGo.AddComponent<Camera>();
            camGo.tag = "MainCamera";
        }

        // Disable Cinemachine brain if present so manual camera positioning works smoothly
        var brain = targetCamera.GetComponent("CinemachineBrain") as MonoBehaviour;
        if (brain != null) brain.enabled = false;

        // 3. Clean up any leftover bag from previous sessions
        GameObject existingBag = GameObject.Find("Solo_HeavyBag");
        if (existingBag != null && !spawnHeavyBag) Destroy(existingBag);
        GameObject existingAnchor = GameObject.Find("BagCeilingAnchor");
        if (existingAnchor != null && !spawnHeavyBag) Destroy(existingAnchor);

        if (spawnHeavyBag && playerTransform != null)
        {
            CreateHeavyBag();
        }
    }

    void Update()
    {
        HandleKeyboardAndMouseInput();
    }

    private void HandleKeyboardAndMouseInput()
    {
#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) currentView = CameraViewMode.MirrorFront;
            if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) currentView = CameraViewMode.SideProfile;
            if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) currentView = CameraViewMode.ThirdPerson;
            if (keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame) currentView = CameraViewMode.FreeOrbit;

            if (keyboard.rKey.wasPressedThisFrame && playerTransform != null)
            {
                playerTransform.position = initialPlayerPos;
                playerTransform.rotation = Quaternion.identity;
            }
        }

        var mouse = Mouse.current;
        if (mouse != null)
        {
            if (currentView == CameraViewMode.FreeOrbit && mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                orbitYaw += delta.x * 0.2f;
                orbitPitch -= delta.y * 0.15f;
                orbitPitch = Mathf.Clamp(orbitPitch, -20f, 60f);
            }

            float scrollY = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scrollY) > 0.01f)
            {
                cameraDistance = Mathf.Clamp(cameraDistance - Mathf.Sign(scrollY) * 0.25f, 1.2f, 5.0f);
            }
        }
#else
        // Legacy input fallback (when project uses Legacy Input Manager)
        if (Input.GetKeyDown(KeyCode.Alpha1)) currentView = CameraViewMode.MirrorFront;
        if (Input.GetKeyDown(KeyCode.Alpha2)) currentView = CameraViewMode.SideProfile;
        if (Input.GetKeyDown(KeyCode.Alpha3)) currentView = CameraViewMode.ThirdPerson;
        if (Input.GetKeyDown(KeyCode.Alpha4)) currentView = CameraViewMode.FreeOrbit;

        if (Input.GetKeyDown(KeyCode.R) && playerTransform != null)
        {
            playerTransform.position = initialPlayerPos;
            playerTransform.rotation = Quaternion.identity;
        }

        if (currentView == CameraViewMode.FreeOrbit && Input.GetMouseButton(1))
        {
            orbitYaw += Input.GetAxis("Mouse X") * 3.5f;
            orbitPitch -= Input.GetAxis("Mouse Y") * 2.5f;
            orbitPitch = Mathf.Clamp(orbitPitch, -20f, 60f);
        }

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            cameraDistance = Mathf.Clamp(cameraDistance - scroll * 2.0f, 1.2f, 5.0f);
        }
#endif
    }

    void LateUpdate()
    {
        if (playerTransform == null || targetCamera == null) return;

        Vector3 focusPoint = playerTransform.position + Vector3.up * cameraHeight;
        Vector3 targetPos = focusPoint;
        Quaternion targetRot = Quaternion.identity;

        switch (currentView)
        {
            case CameraViewMode.MirrorFront:
                // Camera directly in front of player looking at them (Mirror View)
                targetPos = focusPoint + playerTransform.forward * cameraDistance;
                targetRot = Quaternion.LookRotation((focusPoint - targetPos).normalized, Vector3.up);
                break;

            case CameraViewMode.SideProfile:
                // Camera on the right side of the player (matching the phone camera profile)
                targetPos = focusPoint + playerTransform.right * cameraDistance + playerTransform.forward * 0.35f;
                targetRot = Quaternion.LookRotation((focusPoint - targetPos).normalized, Vector3.up);
                break;

            case CameraViewMode.ThirdPerson:
                // Over the shoulder view behind player
                targetPos = focusPoint - playerTransform.forward * cameraDistance + playerTransform.right * 0.35f + Vector3.up * 0.2f;
                targetRot = Quaternion.LookRotation((focusPoint + playerTransform.forward * 2f - targetPos).normalized, Vector3.up);
                break;

            case CameraViewMode.FreeOrbit:
                // Orbit around focus point
                Quaternion orbitRot = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
                targetPos = focusPoint + orbitRot * (Vector3.back * cameraDistance);
                targetRot = Quaternion.LookRotation((focusPoint - targetPos).normalized, Vector3.up);
                break;
        }

        targetCamera.transform.position = Vector3.Lerp(targetCamera.transform.position, targetPos, Time.deltaTime * 18f);
        targetCamera.transform.rotation = Quaternion.Slerp(targetCamera.transform.rotation, targetRot, Time.deltaTime * 18f);

        // Check glove collision with heavy bag if active
        if (heavyBag != null && heavyBag.activeSelf && bagRb != null && avatar != null)
        {
            CheckBagHit(avatar.LeftHandWorldPos, avatar.LeftPunchSpeed);
            CheckBagHit(avatar.RightHandWorldPos, avatar.RightPunchSpeed);
        }
    }

    public void ToggleHeavyBag()
    {
        spawnHeavyBag = !spawnHeavyBag;
        if (spawnHeavyBag)
        {
            if (heavyBag == null) CreateHeavyBag();
            else heavyBag.SetActive(true);
        }
        else
        {
            if (heavyBag != null) heavyBag.SetActive(false);
        }
    }

    private void CreateHeavyBag()
    {
        if (playerTransform == null) return;

        heavyBag = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        heavyBag.name = "Solo_HeavyBag";
        heavyBag.transform.position = playerTransform.position + playerTransform.forward * 1.35f + Vector3.up * 1.5f;
        heavyBag.transform.localScale = new Vector3(0.42f, 1.1f, 0.42f);

        // Dark leather material
        var renderer = heavyBag.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mat.color = new Color(0.18f, 0.12f, 0.10f);
            renderer.material = mat;
        }

        bagRb = heavyBag.AddComponent<Rigidbody>();
        bagRb.mass = 45f;
        bagRb.linearDamping = 1.5f;
        bagRb.angularDamping = 2.0f;

        // Hanging joint
        GameObject ceilingAnchor = new GameObject("BagCeilingAnchor");
        ceilingAnchor.transform.position = heavyBag.transform.position + Vector3.up * 2.2f;
        var anchorRb = ceilingAnchor.AddComponent<Rigidbody>();
        anchorRb.isKinematic = true;

        HingeJoint joint = heavyBag.AddComponent<HingeJoint>();
        joint.connectedBody = anchorRb;
        joint.anchor = new Vector3(0, 1.1f, 0);
        joint.axis = Vector3.right;
    }

    private void CheckBagHit(Vector3 handPos, float punchSpeed)
    {
        if (heavyBag == null || bagRb == null || punchSpeed < 1.6f) return;

        float dist = Vector3.Distance(handPos, heavyBag.transform.position);
        if (dist < 0.55f)
        {
            Vector3 pushDir = (heavyBag.transform.position - handPos).normalized;
            pushDir.y = 0.1f;
            bagRb.AddForce(pushDir * (punchSpeed * 35f), ForceMode.Impulse);
        }
    }

    void OnGUI()
    {
        // Styling
        GUIStyle headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };
        headerStyle.normal.textColor = Color.cyan;

        GUIStyle textStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleLeft
        };
        textStyle.normal.textColor = Color.white;

        GUIStyle pillStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        GUIStyle btnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold
        };

        // Dark background panel on top-left
        GUI.Box(new Rect(15, 15, 430, 275), GUIContent.none);

        GUILayout.BeginArea(new Rect(25, 20, 410, 265));

        GUILayout.Label("🥊 SOLO DUAL-CAMERA TESTING RIG", headerStyle);
        GUILayout.Space(2);

        // Status pill
        GUI.color = Color.green;
        GUILayout.Box("● 3D DUAL-CAM FUSED TRACKING ACTIVE (UDP: 5055)", pillStyle, GUILayout.Height(22));
        GUI.color = Color.white;

        GUILayout.Space(4);

        // Clickable Camera Perspective Buttons
        GUILayout.Label("<b>Switch Perspective (Click or press 1-4):</b>", textStyle);
        GUILayout.BeginHorizontal();
        DrawViewButton("1: Mirror", CameraViewMode.MirrorFront, btnStyle);
        DrawViewButton("2: Side Profile", CameraViewMode.SideProfile, btnStyle);
        DrawViewButton("3: 3rd Person", CameraViewMode.ThirdPerson, btnStyle);
        DrawViewButton("4: Orbit", CameraViewMode.FreeOrbit, btnStyle);
        GUILayout.EndHorizontal();

        GUILayout.Space(4);

        // Action Buttons: Reset & Toggle Bag
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("🔄 Reset Pos (R)", btnStyle, GUILayout.Height(24)))
        {
            if (playerTransform != null)
            {
                playerTransform.position = initialPlayerPos;
                playerTransform.rotation = Quaternion.identity;
            }
        }

        string bagBtnLabel = (heavyBag != null && heavyBag.activeSelf) ? "🥊 Hide Bag" : "🥊 Show Bag";
        if (GUILayout.Button(bagBtnLabel, btnStyle, GUILayout.Height(24)))
        {
            ToggleHeavyBag();
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(4);

        // Telemetry readings from AvatarController
        if (avatar != null)
        {
            float lSpd = avatar.LeftPunchSpeed;
            float rSpd = avatar.RightPunchSpeed;
            string punchType = avatar.CurrentPunchType;

            GUILayout.Label($"<b>Left Hand (Jab):</b> {lSpd:F1} m/s", textStyle);
            DrawMiniBar(lSpd / 7.0f, new Color(0.2f, 0.7f, 1f));

            GUILayout.Label($"<b>Right Hand (Cross):</b> {rSpd:F1} m/s", textStyle);
            DrawMiniBar(rSpd / 7.0f, new Color(1f, 0.4f, 0.2f));

            GUILayout.Label($"<b>Detected Action:</b> <color=yellow>{punchType}</color>", textStyle);
        }

        GUILayout.EndArea();
    }

    private void DrawViewButton(string label, CameraViewMode mode, GUIStyle baseStyle)
    {
        bool isActive = currentView == mode;
        Color oldColor = GUI.backgroundColor;
        if (isActive) GUI.backgroundColor = Color.cyan;
        if (GUILayout.Button(label, baseStyle, GUILayout.Height(28)))
        {
            currentView = mode;
        }
        GUI.backgroundColor = oldColor;
    }

    private void DrawMiniBar(float fillPercent, Color barColor)
    {
        Rect r = GUILayoutUtility.GetRect(280, 7);
        GUI.Box(r, GUIContent.none);
        Rect fill = new Rect(r.x, r.y, Mathf.Clamp01(fillPercent) * r.width, r.height);
        Color oldColor = GUI.color;
        GUI.color = barColor;
        GUI.Box(fill, GUIContent.none);
        GUI.color = oldColor;
    }
}
