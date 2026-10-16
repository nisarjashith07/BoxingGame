using UnityEngine;

namespace BoxingAI
{
    /// <summary>
    /// Automatically bootstraps the entire combat system when Play Mode starts:
    /// 1. Disables free-roam ThirdPersonController and enables dedicated BoxingPlayerController.
    /// 2. Spawns/Ensures AI Fighter opponent facing player in the pocket (1.5m away).
    /// 3. Ensures PunchDetector is active and wired.
    /// 4. Generates full Combat Canvas & HUD (health bars, numeric HP, floating damage, overhead bar).
    /// 5. Initializes BoxingEnvironment match loop.
    /// </summary>
    public static class CombatBootstrapper
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void OnSceneLoaded()
        {
            EnsureCombatSystem();
        }

        public static void EnsureCombatSystem()
        {
            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            // Solo Camera Testing scene check: do not spawn opponent or combat HUD
            if (sceneName.Contains("Solo") || sceneName.Contains("CameraTest") || sceneName.Contains("SampleScene") || Object.FindAnyObjectByType<SoloCameraTestManager>() != null)
            {
                Debug.Log($"[CombatBootstrapper] Active scene '{sceneName}' is in Solo Testing Mode. Opponent and combat match will not be spawned.");
                return;
            }

            // 1. Find Player
            AvatarController playerAvatar = Object.FindAnyObjectByType<AvatarController>();
            Transform playerTransform = playerAvatar != null
                ? playerAvatar.transform
                : GameObject.Find("PlayerArmature")?.transform;

            if (playerTransform == null)
            {
                Debug.LogWarning("[CombatBootstrapper] PlayerArmature not found in scene. Combat not bootstrapped.");
                return;
            }

            // Disable free-roam ThirdPersonController so it doesn't turn away on WASD
            var tpc = playerTransform.GetComponent<StarterAssets.ThirdPersonController>();
            if (tpc != null)
            {
                tpc.enabled = false;
                Debug.Log("[CombatBootstrapper] Disabled free-roam ThirdPersonController.");
            }

            // Attach dedicated BoxingPlayerController for tight boxing footwork
            BoxingPlayerController playerCtrl = playerTransform.GetComponent<BoxingPlayerController>();
            if (playerCtrl == null)
                playerCtrl = playerTransform.gameObject.AddComponent<BoxingPlayerController>();
            playerCtrl.enabled = true;
            Debug.Log("[CombatBootstrapper] Enabled BoxingPlayerController on PlayerArmature.");

            // 2. Ensure CombatSettings
            CombatSettings settings = CombatSettings.CreateDefault();

            // 3. Ensure AI Fighter
            AIFighterController aiFighter = Object.FindAnyObjectByType<AIFighterController>();
            AIOpponentController aiOpponent = Object.FindAnyObjectByType<AIOpponentController>();

            if (aiFighter == null)
            {
                GameObject aiGo = GameObject.Find("AIFighter");
                if (aiGo == null)
                {
                    aiGo = Object.Instantiate(playerTransform.gameObject);
                    aiGo.name = "AIFighter";

                    // Position facing player, 1.5m away (ideal boxing pocket range)
                    aiGo.transform.position = playerTransform.position + playerTransform.forward * 1.5f;
                    aiGo.transform.rotation = Quaternion.LookRotation(-playerTransform.forward, Vector3.up);

                    // Remove player-specific controllers from AI clone
                    var avatarCtrl = aiGo.GetComponent<AvatarController>();
                    if (avatarCtrl != null) Object.DestroyImmediate(avatarCtrl);

                    var bpc = aiGo.GetComponent<BoxingPlayerController>();
                    if (bpc != null) Object.DestroyImmediate(bpc);

                    var allBehaviours = aiGo.GetComponents<MonoBehaviour>();
                    foreach (var b in allBehaviours)
                    {
                        if (b != null)
                        {
                            string tName = b.GetType().Name;
                            if (tName.Contains("ThirdPerson") || tName.Contains("StarterAssets") || tName.Contains("Input"))
                            {
                                Object.DestroyImmediate(b);
                            }
                        }
                    }

                    // Tint AI character crimson red so it clearly looks like an opponent
                    var renderers = aiGo.GetComponentsInChildren<Renderer>();
                    foreach (var r in renderers)
                    {
                        if (r is SkinnedMeshRenderer smr)
                        {
                            Material[] mats = smr.materials;
                            for (int i = 0; i < mats.Length; i++)
                            {
                                if (mats[i].HasProperty("_BaseColor"))
                                    mats[i].SetColor("_BaseColor", new Color(0.9f, 0.25f, 0.2f));
                                else if (mats[i].HasProperty("_Color"))
                                    mats[i].SetColor("_Color", new Color(0.9f, 0.25f, 0.2f));
                            }
                            smr.materials = mats;
                        }
                    }

                    // Configure CharacterController on AI
                    CharacterController cc = aiGo.GetComponent<CharacterController>();
                    if (cc == null) cc = aiGo.AddComponent<CharacterController>();
                    cc.height = 1.8f;
                    cc.radius = 0.3f;
                    cc.center = new Vector3(0, 0.9f, 0);

                    // Add AI fighter components
                    aiFighter = aiGo.AddComponent<AIFighterController>();
                    aiOpponent = aiGo.AddComponent<AIOpponentController>();
                    aiOpponent.SetDifficulty(AIDifficulty.Medium);

                    Debug.Log("[CombatBootstrapper] Created AIFighter opponent in front of player.");
                }
                else
                {
                    aiFighter = aiGo.GetComponent<AIFighterController>();
                    if (aiFighter == null) aiFighter = aiGo.AddComponent<AIFighterController>();

                    aiOpponent = aiGo.GetComponent<AIOpponentController>();
                    if (aiOpponent == null) aiOpponent = aiGo.AddComponent<AIOpponentController>();
                }
            }

            // Wire target transforms
            playerCtrl.SetOpponent(aiFighter.transform);
            aiFighter.SetPlayerTarget(playerTransform);

            // 4. Ensure PunchDetector
            PunchDetector punchDetector = Object.FindAnyObjectByType<PunchDetector>();
            if (punchDetector == null)
            {
                GameObject netMgr = GameObject.Find("NetworkManager");
                GameObject host = netMgr != null ? netMgr : playerTransform.gameObject;
                punchDetector = host.AddComponent<PunchDetector>();
                punchDetector.Initialize(settings, playerAvatar, playerTransform);
                Debug.Log("[CombatBootstrapper] Added PunchDetector.");
            }

            // 5. Ensure CombatHUD
            CombatHUD hud = CombatHUD.EnsureHUD();

            // 6. Ensure BoxingEnvironment
            BoxingEnvironment env = Object.FindAnyObjectByType<BoxingEnvironment>();
            if (env == null)
            {
                GameObject envGo = new GameObject("BoxingEnvironment");
                env = envGo.AddComponent<BoxingEnvironment>();
                env.AutoFindReferences();
                Debug.Log("[CombatBootstrapper] Created BoxingEnvironment.");
            }

            // Wire everything
            MatchTimer timer = Object.FindAnyObjectByType<MatchTimer>();
            env.Initialize(punchDetector, aiFighter, aiOpponent, settings, hud, timer, playerTransform);
            env.StartMatch();

            Debug.Log("<color=green>[CombatBootstrapper] Boxing Combat System fully bootstrapped and running!</color>");
        }
    }
}
