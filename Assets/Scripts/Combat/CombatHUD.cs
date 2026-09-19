using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BoxingAI
{
    /// <summary>
    /// Complete Boxing Combat HUD:
    /// - Screen-space health bars with numeric hitpoints ("100 / 100 HP")
    /// - Overhead 3D health bar directly above the AI opponent
    /// - 3D floating damage numbers on impacts ("-14 JAB", "-18 HOOK", "BLOCKED -3")
    /// - Dynamic hit feedback banner ("PLAYER: JAB! -8 HP")
    /// - Round timer & match result overlays
    /// - Bottom controls guide banner
    /// - Procedural audio for punch hits and glove blocks
    /// - Fully self-generating: creates Canvas, elements, and fallback fonts if missing
    /// </summary>
    public class CombatHUD : MonoBehaviour
    {
        [Header("Player HUD Elements")]
        [SerializeField] private Image playerHealthFill;
        [SerializeField] private Image playerStaminaFill;
        [SerializeField] private Text playerHPText;
        [SerializeField] private Text playerNameText;

        [Header("Opponent HUD Elements")]
        [SerializeField] private Image opponentHealthFill;
        [SerializeField] private Image opponentStaminaFill;
        [SerializeField] private Text opponentHPText;
        [SerializeField] private Text opponentNameText;

        [Header("Match Info Elements")]
        [SerializeField] private Text roundText;
        [SerializeField] private Text hitFeedbackText;
        [SerializeField] private Text controlsHelpText;

        [Header("Match Result")]
        [SerializeField] private GameObject resultPanel;
        [SerializeField] private Text resultText;

        [Header("Health Bar Colors")]
        [SerializeField] private Color healthyColor = new Color(0.2f, 0.9f, 0.25f);
        [SerializeField] private Color damagedColor = new Color(1.0f, 0.75f, 0.1f);
        [SerializeField] private Color criticalColor = new Color(1.0f, 0.25f, 0.2f);

        // Floating Damage Numbers
        private class FloatingText
        {
            public GameObject go;
            public Text text;
            public Vector3 worldPos;
            public float lifetime;
            public float maxLifetime;
        }
        private readonly List<FloatingText> activeFloatingTexts = new List<FloatingText>();

        // Overhead Opponent Health Bar (Screen-space projection over AI head)
        private GameObject overheadBarGo;
        private Image overheadFill;
        private Text overheadHpText;
        private Transform aiHeadTransform;

        private float feedbackTimer;
        private static AudioClip punchSound;
        private static AudioClip blockSound;
        private static Font cachedFont;

        public static Font DefaultFont
        {
            get
            {
                if (cachedFont == null)
                {
                    cachedFont = Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Segoe UI", "Tahoma", "Calibri" }, 16);
                    if (cachedFont == null)
                        cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
                return cachedFont;
            }
        }

        private void Awake()
        {
            AutoFindReferences();
            GenerateAudioClips();
        }

        private void Start()
        {
            if (resultPanel != null)
                resultPanel.SetActive(false);
            if (resultText != null)
                resultText.gameObject.SetActive(false);
            if (hitFeedbackText != null)
                hitFeedbackText.gameObject.SetActive(false);

            if (playerNameText != null)
                playerNameText.text = "YOU (PLAYER)";
            if (opponentNameText != null)
                opponentNameText.text = "AI OPPONENT";
            if (roundText != null)
                roundText.text = "ROUND 1  •  03:00";
        }

        /// <summary>
        /// Ensures a full Combat HUD exists in the scene, creating Canvas and all UI if missing.
        /// </summary>
        public static CombatHUD EnsureHUD()
        {
            CombatHUD existing = FindAnyObjectByType<CombatHUD>();
            if (existing != null)
            {
                existing.AutoFindReferences();
                return existing;
            }

            // Find or create Canvas
            Canvas canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasGo = new GameObject("CombatCanvas");
                canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 100;

                CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;

                canvasGo.AddComponent<GraphicRaycaster>();
            }

            // Ensure EventSystem
            if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                GameObject esGo = new GameObject("EventSystem");
                esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
                esGo.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }

            CombatHUD hud = canvas.GetComponent<CombatHUD>();
            if (hud == null)
                hud = canvas.gameObject.AddComponent<CombatHUD>();

            hud.AutoFindReferences();
            return hud;
        }

        public void AutoFindReferences()
        {
            // If the core health bars don't exist under this canvas, construct the full UI hierarchy
            if (playerHealthFill == null || opponentHealthFill == null)
            {
                BuildFullUI();
            }

            // Setup overhead health bar over AI
            SetupOverheadHealthBar();
        }

        private void BuildFullUI()
        {
            Transform root = transform;

            // 1. TOP HEADER CONTAINER
            GameObject topBar = CreateUIObject("TopHeaderBar", root,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0, 110));

            Image topBarBg = topBar.AddComponent<Image>();
            topBarBg.color = new Color(0.06f, 0.07f, 0.10f, 0.85f);

            // --- PLAYER (TOP LEFT) ---
            GameObject playerBox = CreateUIObject("PlayerHUD", topBar.transform,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(40, 0), new Vector2(460, 90));

            playerNameText = CreateUIText("PlayerName", playerBox.transform,
                "YOU (PLAYER)", 20, FontStyle.Bold, new Color(0.3f, 0.95f, 0.4f),
                TextAnchor.MiddleLeft, new Vector2(0, 26), new Vector2(450, 28));

            // Health bar frame
            GameObject pHealthBg = CreateUIObject("PlayerHealthBG", playerBox.transform,
                new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                new Vector2(0, -4), new Vector2(450, 26));
            pHealthBg.AddComponent<Image>().color = new Color(0.12f, 0.13f, 0.16f, 0.95f);

            // Health bar fill
            GameObject pHealthFillGo = CreateUIObject("PlayerHealthFill", pHealthBg.transform,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            playerHealthFill = pHealthFillGo.AddComponent<Image>();
            playerHealthFill.type = Image.Type.Filled;
            playerHealthFill.fillMethod = Image.FillMethod.Horizontal;
            playerHealthFill.fillOrigin = 0;
            playerHealthFill.fillAmount = 1f;
            playerHealthFill.color = healthyColor;

            // Health numeric text
            playerHPText = CreateUIText("PlayerHPNumber", pHealthBg.transform,
                "100 / 100 HP", 16, FontStyle.Bold, Color.white,
                TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero);
            var pOutline = playerHPText.gameObject.AddComponent<Outline>();
            pOutline.effectColor = Color.black;
            pOutline.effectDistance = new Vector2(1.2f, -1.2f);

            // Stamina bar frame
            GameObject pStamBg = CreateUIObject("PlayerStaminaBG", playerBox.transform,
                new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                new Vector2(0, -24), new Vector2(340, 12));
            pStamBg.AddComponent<Image>().color = new Color(0.12f, 0.13f, 0.16f, 0.95f);

            // Stamina fill
            GameObject pStamFillGo = CreateUIObject("PlayerStaminaFill", pStamBg.transform,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            playerStaminaFill = pStamFillGo.AddComponent<Image>();
            playerStaminaFill.type = Image.Type.Filled;
            playerStaminaFill.fillMethod = Image.FillMethod.Horizontal;
            playerStaminaFill.fillOrigin = 0;
            playerStaminaFill.fillAmount = 1f;
            playerStaminaFill.color = new Color(0.2f, 0.75f, 1f);

            // --- ROUND & TIMER (TOP CENTER) ---
            GameObject roundBox = CreateUIObject("RoundBox", topBar.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240, 60));
            roundBox.AddComponent<Image>().color = new Color(0.1f, 0.12f, 0.18f, 0.9f);

            roundText = CreateUIText("RoundText", roundBox.transform,
                "ROUND 1\n03:00", 18, FontStyle.Bold, new Color(1f, 0.85f, 0.2f),
                TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero);
            var rOutline = roundText.gameObject.AddComponent<Outline>();
            rOutline.effectColor = Color.black;
            rOutline.effectDistance = new Vector2(1.2f, -1.2f);

            // --- AI OPPONENT (TOP RIGHT) ---
            GameObject aiBox = CreateUIObject("AIOpponentHUD", topBar.transform,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(-40, 0), new Vector2(460, 90));

            opponentNameText = CreateUIText("OpponentName", aiBox.transform,
                "AI OPPONENT", 20, FontStyle.Bold, new Color(1f, 0.35f, 0.3f),
                TextAnchor.MiddleRight, new Vector2(0, 26), new Vector2(450, 28));

            // Health bar frame
            GameObject aiHealthBg = CreateUIObject("OpponentHealthBG", aiBox.transform,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(0, -4), new Vector2(450, 26));
            aiHealthBg.AddComponent<Image>().color = new Color(0.12f, 0.13f, 0.16f, 0.95f);

            // Health bar fill
            GameObject aiHealthFillGo = CreateUIObject("OpponentHealthFill", aiHealthBg.transform,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            opponentHealthFill = aiHealthFillGo.AddComponent<Image>();
            opponentHealthFill.type = Image.Type.Filled;
            opponentHealthFill.fillMethod = Image.FillMethod.Horizontal;
            opponentHealthFill.fillOrigin = 1; // Drain right-to-left
            opponentHealthFill.fillAmount = 1f;
            opponentHealthFill.color = healthyColor;

            // Health numeric text
            opponentHPText = CreateUIText("OpponentHPNumber", aiHealthBg.transform,
                "100 / 100 HP", 16, FontStyle.Bold, Color.white,
                TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero);
            var aiOutline = opponentHPText.gameObject.AddComponent<Outline>();
            aiOutline.effectColor = Color.black;
            aiOutline.effectDistance = new Vector2(1.2f, -1.2f);

            // Stamina bar frame
            GameObject aiStamBg = CreateUIObject("OpponentStaminaBG", aiBox.transform,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(0, -24), new Vector2(340, 12));
            aiStamBg.AddComponent<Image>().color = new Color(0.12f, 0.13f, 0.16f, 0.95f);

            // Stamina fill
            GameObject aiStamFillGo = CreateUIObject("OpponentStaminaFill", aiStamBg.transform,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            opponentStaminaFill = aiStamFillGo.AddComponent<Image>();
            opponentStaminaFill.type = Image.Type.Filled;
            opponentStaminaFill.fillMethod = Image.FillMethod.Horizontal;
            opponentStaminaFill.fillOrigin = 1;
            opponentStaminaFill.fillAmount = 1f;
            opponentStaminaFill.color = new Color(0.2f, 0.75f, 1f);

            // --- HIT FEEDBACK BANNER (UPPER CENTER) ---
            hitFeedbackText = CreateUIText("HitFeedbackText", root,
                "", 28, FontStyle.Bold, Color.yellow,
                TextAnchor.MiddleCenter, new Vector2(0, 360), new Vector2(800, 60));
            var hOutline = hitFeedbackText.gameObject.AddComponent<Outline>();
            hOutline.effectColor = Color.black;
            hOutline.effectDistance = new Vector2(1.8f, -1.8f);
            hitFeedbackText.gameObject.SetActive(false);

            // --- CONTROLS GUIDE (BOTTOM BAR) ---
            GameObject botBar = CreateUIObject("BottomControlsBar", root,
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0, 42));
            botBar.AddComponent<Image>().color = new Color(0.06f, 0.07f, 0.10f, 0.85f);

            controlsHelpText = CreateUIText("ControlsText", botBar.transform,
                "[WASD] Boxing Footwork  •  [Left Click / J] Jab  •  [Right Click / K] Cross  •  [H/L] Hooks  •  [Space] Guard",
                15, FontStyle.Bold, new Color(0.9f, 0.92f, 0.95f, 0.9f),
                TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero);

            // --- MATCH RESULT OVERLAY PANEL ---
            resultPanel = CreateUIObject("ResultPanel", root,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            resultPanel.AddComponent<Image>().color = new Color(0.02f, 0.03f, 0.05f, 0.85f);

            resultText = CreateUIText("ResultText", resultPanel.transform,
                "KNOCKOUT!\nVICTORY!", 56, FontStyle.Bold, new Color(1f, 0.85f, 0.2f),
                TextAnchor.MiddleCenter, Vector2.zero, new Vector2(900, 200));
            var resOutline = resultText.gameObject.AddComponent<Outline>();
            resOutline.effectColor = Color.black;
            resOutline.effectDistance = new Vector2(3f, -3f);

            resultPanel.SetActive(false);
            resultText.gameObject.SetActive(false);

            Debug.Log("[CombatHUD] Full boxing UI hierarchy created successfully.");
        }

        private void SetupOverheadHealthBar()
        {
            if (overheadBarGo != null) return;

            AIFighterController ai = FindAnyObjectByType<AIFighterController>();
            if (ai != null)
                aiHeadTransform = ai.transform;

            overheadBarGo = CreateUIObject("OverheadHealthBar", transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(170, 22));

            // Background
            overheadBarGo.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.12f, 0.85f);

            // Fill
            GameObject fillGo = CreateUIObject("Fill", overheadBarGo.transform,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            overheadFill = fillGo.AddComponent<Image>();
            overheadFill.type = Image.Type.Filled;
            overheadFill.fillMethod = Image.FillMethod.Horizontal;
            overheadFill.fillOrigin = 0;
            overheadFill.fillAmount = 1f;
            overheadFill.color = healthyColor;

            // Text
            overheadHpText = CreateUIText("Text", overheadBarGo.transform,
                "AI: 100 HP", 12, FontStyle.Bold, Color.white,
                TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero);
            var oOutline = overheadHpText.gameObject.AddComponent<Outline>();
            oOutline.effectColor = Color.black;
            oOutline.effectDistance = new Vector2(1f, -1f);

            overheadBarGo.SetActive(false);
        }

        private void Update()
        {
            // Hit banner timer
            if (feedbackTimer > 0f)
            {
                feedbackTimer -= Time.deltaTime;
                if (feedbackTimer <= 0f && hitFeedbackText != null)
                    hitFeedbackText.gameObject.SetActive(false);
            }

            // Update floating damage numbers
            UpdateFloatingTexts();

            // Update overhead health bar position over AI head
            UpdateOverheadPosition();
        }

        private void UpdateOverheadPosition()
        {
            if (overheadBarGo == null)
            {
                SetupOverheadHealthBar();
                return;
            }

            if (aiHeadTransform == null)
            {
                AIFighterController ai = FindAnyObjectByType<AIFighterController>();
                if (ai != null) aiHeadTransform = ai.transform;
                return;
            }

            Camera cam = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
            if (cam != null)
            {
                Vector3 headWorld = aiHeadTransform.position + Vector3.up * 2.15f;
                Vector3 screenPos = cam.WorldToScreenPoint(headWorld);

                if (screenPos.z > 0.5f)
                {
                    overheadBarGo.SetActive(true);
                    RectTransform rt = overheadBarGo.GetComponent<RectTransform>();
                    rt.position = screenPos;
                }
                else
                {
                    overheadBarGo.SetActive(false);
                }
            }
        }

        private void UpdateFloatingTexts()
        {
            Camera cam = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();

            for (int i = activeFloatingTexts.Count - 1; i >= 0; i--)
            {
                var ft = activeFloatingTexts[i];
                ft.lifetime -= Time.deltaTime;

                if (ft.lifetime <= 0f)
                {
                    Destroy(ft.go);
                    activeFloatingTexts.RemoveAt(i);
                    continue;
                }

                // Float upward in world space
                ft.worldPos += Vector3.up * (0.85f * Time.deltaTime);

                if (cam != null)
                {
                    Vector3 screenPos = cam.WorldToScreenPoint(ft.worldPos);
                    if (screenPos.z > 0.3f)
                    {
                        ft.go.SetActive(true);
                        ft.go.transform.position = screenPos;

                        // Fade alpha in second half of lifetime
                        float alpha = Mathf.Clamp01(ft.lifetime / (ft.maxLifetime * 0.5f));
                        Color c = ft.text.color;
                        c.a = alpha;
                        ft.text.color = c;
                    }
                    else
                    {
                        ft.go.SetActive(false);
                    }
                }
            }
        }

        /// <summary>
        /// Called each frame by BoxingEnvironment to update health and stamina.
        /// </summary>
        public void UpdateHUD(FighterState player, FighterState ai)
        {
            // Player Top Health Bar
            if (playerHealthFill != null)
            {
                playerHealthFill.fillAmount = player.Health01;
                playerHealthFill.color = GetHealthColor(player.Health01);
            }
            if (playerHPText != null)
            {
                playerHPText.text = $"{Mathf.CeilToInt(player.health)} / 100 HP";
            }

            // Opponent Top Health Bar
            if (opponentHealthFill != null)
            {
                opponentHealthFill.fillAmount = ai.Health01;
                opponentHealthFill.color = GetHealthColor(ai.Health01);
            }
            if (opponentHPText != null)
            {
                opponentHPText.text = $"{Mathf.CeilToInt(ai.health)} / 100 HP";
            }

            // Overhead 3D Health Bar on AI
            if (overheadFill != null)
            {
                overheadFill.fillAmount = ai.Health01;
                overheadFill.color = GetHealthColor(ai.Health01);
            }
            if (overheadHpText != null)
            {
                overheadHpText.text = $"AI: {Mathf.CeilToInt(ai.health)} HP";
            }

            if (playerStaminaFill != null)
                playerStaminaFill.fillAmount = player.Stamina01;

            if (opponentStaminaFill != null)
                opponentStaminaFill.fillAmount = ai.Stamina01;
        }

        /// <summary>
        /// Trigger visual and sound feedback when a hit lands.
        /// </summary>
        public void OnHit(string attacker, BoxingAction punch, float damage, bool blocked, Vector3 hitWorldPos)
        {
            PlayImpactSound(blocked);

            // Spawn 3D Floating Damage Number
            SpawnFloatingDamage(hitWorldPos, damage, punch, attacker == "PLAYER", blocked);

            // Top Banner Feedback
            if (hitFeedbackText != null)
            {
                hitFeedbackText.gameObject.SetActive(true);
                feedbackTimer = 1.1f;

                if (blocked)
                {
                    hitFeedbackText.color = new Color(0.3f, 0.8f, 1f);
                    hitFeedbackText.text = $"{attacker}'s {punch.ToString().ToUpper()} BLOCKED! (-{damage:F0} HP)";
                }
                else if (attacker == "PLAYER")
                {
                    hitFeedbackText.color = new Color(0.3f, 0.95f, 0.3f);
                    hitFeedbackText.text = $"PLAYER HIT! {punch.ToString().ToUpper()} -{damage:F0} HP";
                }
                else
                {
                    hitFeedbackText.color = new Color(1f, 0.35f, 0.3f);
                    hitFeedbackText.text = $"AI HIT! {punch.ToString().ToUpper()} -{damage:F0} HP";
                }
            }
        }

        private void SpawnFloatingDamage(Vector3 worldPos, float damage, BoxingAction punch, bool isPlayerHit, bool blocked)
        {
            GameObject go = CreateUIObject("FloatingDamage", transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(280, 44));

            Text txt = go.AddComponent<Text>();
            txt.font = DefaultFont;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.fontSize = blocked ? 20 : 28;
            txt.fontStyle = FontStyle.Bold;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            if (blocked)
            {
                txt.color = new Color(0.3f, 0.8f, 1f);
                txt.text = $"BLOCKED -{damage:F0}";
            }
            else if (isPlayerHit)
            {
                txt.color = new Color(1f, 0.9f, 0.15f); // Gold
                txt.text = $"-{damage:F0} {punch.ToString().ToUpper()}!";
            }
            else
            {
                txt.color = new Color(1f, 0.3f, 0.25f); // Red
                txt.text = $"-{damage:F0} {punch.ToString().ToUpper()}";
            }

            Vector3 startPos = worldPos + Vector3.up * 0.2f + (Random.insideUnitSphere * 0.1f);
            Camera cam = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
            if (cam != null)
                go.transform.position = cam.WorldToScreenPoint(startPos);

            activeFloatingTexts.Add(new FloatingText
            {
                go = go,
                text = txt,
                worldPos = startPos,
                lifetime = 0.95f,
                maxLifetime = 0.95f
            });
        }

        public void ShowResult(string result)
        {
            if (resultText != null)
            {
                resultText.text = result;
                resultText.gameObject.SetActive(true);
            }
            if (resultPanel != null)
                resultPanel.SetActive(true);

            if (hitFeedbackText != null)
            {
                hitFeedbackText.gameObject.SetActive(true);
                hitFeedbackText.color = Color.yellow;
                hitFeedbackText.text = result;
            }
        }

        private Color GetHealthColor(float health01)
        {
            if (health01 > 0.5f) return healthyColor;
            if (health01 > 0.25f) return damagedColor;
            return criticalColor;
        }

        // --- UI Construction Helpers ---

        private static GameObject CreateUIObject(
            string name, Transform parent,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPos, Vector2 sizeDelta)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;
            return go;
        }

        private static Text CreateUIText(
            string name, Transform parent,
            string text, int fontSize, FontStyle style, Color color,
            TextAnchor alignment, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            GameObject go = CreateUIObject(name, parent,
                sizeDelta == Vector2.zero ? Vector2.zero : new Vector2(0.5f, 0.5f),
                sizeDelta == Vector2.zero ? Vector2.one : new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), anchoredPos, sizeDelta);

            Text t = go.AddComponent<Text>();
            t.font = DefaultFont;
            t.text = text;
            t.fontSize = fontSize;
            t.fontStyle = style;
            t.color = color;
            t.alignment = alignment;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        // --- Audio ---

        private static void GenerateAudioClips()
        {
            if (punchSound != null) return;

            int rate = 44100;
            int length = rate / 6; // ~160ms
            float[] punchData = new float[length];
            float[] blockData = new float[length];

            for (int i = 0; i < length; i++)
            {
                float t = (float)i / rate;
                float freq = Mathf.Lerp(180f, 35f, t / 0.16f);
                float env = Mathf.Exp(-t * 22f);
                float tone = Mathf.Sin(2f * Mathf.PI * freq * t);
                float noise = (Random.value * 2f - 1f) * Mathf.Exp(-t * 35f);
                punchData[i] = Mathf.Clamp(tone * 0.75f * env + noise * 0.25f, -1f, 1f);

                float blockFreq = Mathf.Lerp(280f, 80f, t / 0.16f);
                float blockEnv = Mathf.Exp(-t * 35f);
                blockData[i] = Mathf.Clamp(Mathf.Sin(2f * Mathf.PI * blockFreq * t) * blockEnv, -1f, 1f);
            }

            punchSound = AudioClip.Create("PunchImpact", length, 1, rate, false);
            punchSound.SetData(punchData, 0);

            blockSound = AudioClip.Create("BlockImpact", length, 1, rate, false);
            blockSound.SetData(blockData, 0);
        }

        private static void PlayImpactSound(bool blocked)
        {
            AudioClip clip = blocked ? blockSound : punchSound;
            if (clip != null)
            {
                Camera cam = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
                AudioSource.PlayClipAtPoint(clip, cam != null ? cam.transform.position : Vector3.zero, 0.85f);
            }
        }
    }
}
