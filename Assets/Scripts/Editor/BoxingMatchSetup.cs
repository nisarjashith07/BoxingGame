#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;

namespace BoxingAI.Editor
{
    public static class BoxingMatchSetup
    {
        [MenuItem("Tools/Boxing Game/Setup Match in Current Scene")]
        public static void SetupCurrentScene()
        {
            // 1. Find Player
            AvatarController playerAvatar = Object.FindAnyObjectByType<AvatarController>();
            Transform playerTransform = playerAvatar != null
                ? playerAvatar.transform
                : GameObject.Find("PlayerArmature")?.transform;

            if (playerTransform == null)
            {
                EditorUtility.DisplayDialog("Setup Error", "Could not find 'PlayerArmature' in the scene!", "OK");
                return;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Setup Boxing Match");
            int undoGroup = Undo.GetCurrentGroup();

            // Disable free-roam ThirdPersonController
            var tpc = playerTransform.GetComponent<StarterAssets.ThirdPersonController>();
            if (tpc != null)
            {
                Undo.RecordObject(tpc, "Disable ThirdPersonController");
                tpc.enabled = false;
            }

            // Ensure BoxingPlayerController
            BoxingPlayerController playerCtrl = playerTransform.GetComponent<BoxingPlayerController>();
            if (playerCtrl == null)
                playerCtrl = Undo.AddComponent<BoxingPlayerController>(playerTransform.gameObject);
            playerCtrl.enabled = true;

            // 2. Ensure CombatSettings asset
            string settingsPath = "Assets/Settings/CombatSettings.asset";
            CombatSettings settings = AssetDatabase.LoadAssetAtPath<CombatSettings>(settingsPath);
            if (settings == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Settings"))
                    AssetDatabase.CreateFolder("Assets", "Settings");

                settings = ScriptableObject.CreateInstance<CombatSettings>();
                AssetDatabase.CreateAsset(settings, settingsPath);
                AssetDatabase.SaveAssets();
            }

            // 3. Create or find AIFighter
            GameObject aiGo = GameObject.Find("AIFighter");
            if (aiGo == null)
            {
                aiGo = Object.Instantiate(playerTransform.gameObject);
                aiGo.name = "AIFighter";
                Undo.RegisterCreatedObjectUndo(aiGo, "Create AIFighter");

                // Position in front of player
                aiGo.transform.position = playerTransform.position + playerTransform.forward * 1.5f;
                aiGo.transform.rotation = Quaternion.LookRotation(-playerTransform.forward, Vector3.up);

                // Remove player-specific components
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

                // Tint materials red
                var renderers = aiGo.GetComponentsInChildren<Renderer>();
                foreach (var r in renderers)
                {
                    if (r is SkinnedMeshRenderer smr)
                    {
                        Material[] mats = smr.sharedMaterials;
                        Material[] newMats = new Material[mats.Length];
                        for (int i = 0; i < mats.Length; i++)
                        {
                            newMats[i] = new Material(mats[i]);
                            if (newMats[i].HasProperty("_BaseColor"))
                                newMats[i].SetColor("_BaseColor", new Color(0.9f, 0.25f, 0.2f));
                            else if (newMats[i].HasProperty("_Color"))
                                newMats[i].SetColor("_Color", new Color(0.9f, 0.25f, 0.2f));
                        }
                        smr.sharedMaterials = newMats;
                    }
                }
            }

            // Ensure AI components
            AIFighterController aiFighter = aiGo.GetComponent<AIFighterController>();
            if (aiFighter == null) aiFighter = aiGo.AddComponent<AIFighterController>();

            AIOpponentController aiOpponent = aiGo.GetComponent<AIOpponentController>();
            if (aiOpponent == null) aiOpponent = aiGo.AddComponent<AIOpponentController>();

            // Wire target transforms
            playerCtrl.SetOpponent(aiFighter.transform);
            aiFighter.SetPlayerTarget(playerTransform);

            // 4. PunchDetector on NetworkManager
            GameObject netMgr = GameObject.Find("NetworkManager");
            GameObject host = netMgr != null ? netMgr : playerTransform.gameObject;
            PunchDetector punchDetector = host.GetComponent<PunchDetector>();
            if (punchDetector == null) punchDetector = host.AddComponent<PunchDetector>();
            punchDetector.Initialize(settings, playerAvatar, playerTransform);

            // 5. CombatHUD
            CombatHUD hud = CombatHUD.EnsureHUD();

            // 6. BoxingEnvironment
            GameObject envGo = GameObject.Find("BoxingEnvironment");
            if (envGo == null)
            {
                envGo = new GameObject("BoxingEnvironment");
                Undo.RegisterCreatedObjectUndo(envGo, "Create BoxingEnvironment");
            }
            BoxingEnvironment env = envGo.GetComponent<BoxingEnvironment>();
            if (env == null) env = envGo.AddComponent<BoxingEnvironment>();

            MatchTimer timer = Object.FindAnyObjectByType<MatchTimer>();
            env.Initialize(punchDetector, aiFighter, aiOpponent, settings, hud, timer, playerTransform);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = envGo;

            EditorUtility.DisplayDialog(
                "Setup Complete!",
                "Boxing match successfully set up in current scene!\n\n" +
                "• BoxingPlayerController enabled on Player (WASD footwork)\n" +
                "• ThirdPersonController disabled\n" +
                "• AIFighter opponent placed in front of player\n" +
                "• Full Combat HUD generated with health bars and numeric HP\n" +
                "• BoxingEnvironment configured\n\n" +
                "Hit Play to box!",
                "Let's Go!");
        }
    }
}
#endif
