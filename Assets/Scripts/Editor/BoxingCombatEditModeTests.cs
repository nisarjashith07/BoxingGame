using System;
using System.IO;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BoxingAI.Tests
{
    /// <summary>
    /// Automated EditMode test suite for combat contracts, action costs,
    /// timestamped tracking, and neural policy inference.
    /// </summary>
    public class BoxingCombatEditModeTests
    {
        [Test]
        public void Fnv1aSchemaHash_MatchesCanonicalContract()
        {
            uint hash = RLPolicyNetwork.ComputeFnv1a32(RLPolicyNetwork.CANONICAL_CONTRACT);
            Assert.AreEqual(RLPolicyNetwork.EXPECTED_SCHEMA_HASH, hash,
                $"FNV-1a schema hash 0x{hash:X8} does not match 0x{RLPolicyNetwork.EXPECTED_SCHEMA_HASH:X8}");
        }

        [Test]
        public void RLPolicyNetwork_LoadsShippedResourceAsset_AndPredictsDeterministically()
        {
            TextAsset asset = Resources.Load<TextAsset>("boxing_ai_policy");
            Assert.IsNotNull(asset, "boxing_ai_policy.bytes asset not found in Assets/Resources/");
            Assert.Greater(asset.bytes.Length, 8, "Policy file is empty");

            RLPolicyNetwork network = RLPolicyNetwork.LoadFromBytes(asset.bytes);
            Assert.IsTrue(network.IsLoaded);
            Assert.AreEqual(18u, network.ObsDim);
            Assert.AreEqual(12u, network.ActionDim);

            // Test evaluation with neutral guard observation
            float[] obs = new float[18];
            obs[0] = 0.15f; // distance ~1.5m / 10m
            obs[1] = 1.0f;  // selfHealth
            obs[2] = 1.0f;  // selfStamina
            obs[3] = 1.0f;  // oppHealth
            obs[4] = 1.0f;  // oppStamina
            obs[5] = 1.0f;  // selfGuard
            obs[6] = 1.0f;  // oppGuard
            obs[14] = 1.0f; // selfCanAttack
            obs[15] = 1.0f; // selfCanDodge
            obs[16] = 1.0f; // selfCanBlock

            BoxingAction action = network.Predict(obs, out float[] logits);
            Assert.IsNotNull(logits);
            Assert.AreEqual(12, logits.Length);

            // Verify logits are finite
            for (int i = 0; i < logits.Length; i++)
            {
                Assert.IsFalse(float.IsNaN(logits[i]), $"Logit at index {i} is NaN");
                Assert.IsFalse(float.IsInfinity(logits[i]), $"Logit at index {i} is Infinity");
            }

            Assert.IsTrue(Enum.IsDefined(typeof(BoxingAction), action), $"Predicted action {(int)action} is not a valid BoxingAction");
        }

        [Test]
        public void RLPolicyNetwork_RejectsCorruptedHeader()
        {
            byte[] corrupted = new byte[64];
            corrupted[0] = 0xFF; // bad magic
            Assert.Throws<InvalidDataException>(() => RLPolicyNetwork.LoadFromBytes(corrupted));
        }

        [Test]
        public void AIFighter_EventBasedStaminaAndCooldowns_PreventRepeatedSpam()
        {
            var go = new GameObject("TestAI");
            var fighter = go.AddComponent<AIFighterController>();
            var settings = CombatSettings.CreateDefault();
            fighter.Initialize(settings);

            var state = new FighterState { health = 100f, stamina = 100f };
            state.Reset();

            Assert.IsTrue(state.CanAttack);
            Assert.AreEqual(100f, state.stamina);

            // 1. Initial Jab costs 12 stamina and starts cooldown
            fighter.ExecuteAction(BoxingAction.Jab, state);
            Assert.AreEqual(100f - settings.punchStaminaCost, state.stamina, 0.01f);
            Assert.IsFalse(state.CanAttack, "Fighter should be on attack cooldown after initiating punch");

            // 2. Immediate repeated action MUST NOT bypass cooldown or double-drain stamina
            float staminaAfterFirst = state.stamina;
            fighter.ExecuteAction(BoxingAction.Jab, state);
            Assert.AreEqual(staminaAfterFirst, state.stamina, 0.001f, "Repeated action while on cooldown should not drain stamina");

            // 3. Dodge costs 20 stamina
            state.Reset();
            fighter.ExecuteAction(BoxingAction.DodgeLeft, state);
            Assert.AreEqual(100f - settings.dodgeStaminaCost, state.stamina, 0.01f);
            Assert.IsFalse(state.CanDodge, "Fighter should be on dodge cooldown");

            // Clean up
            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(settings);
        }

        [Test]
        public void PunchDetector_IgnoresLowVisibilityLandmarks()
        {
            var go = new GameObject("TestDetector");
            var detector = go.AddComponent<PunchDetector>();
            var settings = CombatSettings.CreateDefault();
            detector.Initialize(settings);

            // Feed frame with low wrist visibility (< 0.25)
            var pose = new PoseData
            {
                frame = 10,
                timestamp = 1.0,
                landmarks = new System.Collections.Generic.List<Landmark>()
            };
            for (int i = 0; i < 33; i++)
            {
                pose.landmarks.Add(new Landmark { x = 0, y = 0, z = 0, visibility = 0.1f });
            }

            detector.ProcessPose(pose);
            Assert.IsFalse(detector.IsPunching, "Low visibility landmark should not trigger punch detection");

            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(settings);
        }

        [Test]
        public void PunchDetector_ResetsOnTrackingGaps_PreventingTeleportPunch()
        {
            var go = new GameObject("TestDetectorGap");
            var detector = go.AddComponent<PunchDetector>();
            var settings = CombatSettings.CreateDefault();
            detector.Initialize(settings);

            // Initial stance frame at t = 1.0s
            var pose1 = new PoseData
            {
                frame = 1,
                timestamp = 1.0,
                landmarks = new System.Collections.Generic.List<Landmark>()
            };
            for (int i = 0; i < 33; i++)
                pose1.landmarks.Add(new Landmark { x = 0, y = 0, z = 0, visibility = 0.9f });
            detector.ProcessPose(pose1);

            // Frame after massive 1.5s tracking gap (player walked into booth) at t = 2.5s with extended arm
            var pose2 = new PoseData
            {
                frame = 2,
                timestamp = 2.5,
                landmarks = new System.Collections.Generic.List<Landmark>()
            };
            for (int i = 0; i < 33; i++)
                pose2.landmarks.Add(new Landmark { x = 0, y = 0, z = -0.8f, visibility = 0.9f });

            detector.ProcessPose(pose2);
            // Gap > 250ms resets history; punch speed must be neutralized to 0 rather than calculating 0.8m / 0.016s teleport velocity!
            Assert.AreEqual(0f, detector.MaxPunchSpeed, 0.01f, "Gap > 250ms must reset punch speed to prevent teleport punch");
            Assert.IsFalse(detector.IsPunching, "Gap > 250ms must not trigger a punch");

            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(settings);
        }

        [Test]
        public void ObservationBuilder_InputBufferMapping_ProducesValidRanges()
        {
            var obs = new BoxingObservation
            {
                distance = 2.5f,
                selfHealth01 = 0.8f,
                selfStamina01 = 0.6f,
                opponentHealth01 = 0.9f,
                opponentStamina01 = 0.7f,
                selfGuard01 = 1.0f,
                opponentGuard01 = 0.5f,
                relativeX = -0.3f,
                relativeZ = 1.2f,
                relativeVelocityX = 0.4f,
                relativeVelocityZ = -0.6f,
                opponentAttackIntent01 = 1.0f,
                incomingAttack = true,
                opponentRecovering = false,
                selfCanAttack = true,
                selfCanDodge = true,
                selfCanBlock = true,
                roundTime01 = 0.5f
            };

            float[] buffer = new float[18];
            RLOpponentController.BuildInputBuffer(obs, buffer);

            Assert.AreEqual(18, buffer.Length);
            for (int i = 0; i < buffer.Length; i++)
            {
                Assert.IsTrue(buffer[i] >= -1.0f && buffer[i] <= 1.0f,
                    $"Buffer element at {i} ({buffer[i]}) is outside [-1.0, 1.0]");
            }

            Assert.AreEqual(0.25f, buffer[0], 0.001f); // distance 2.5 / 10.0
            Assert.AreEqual(0.8f, buffer[1], 0.001f);  // selfHealth01
            Assert.AreEqual(0.4f, buffer[9], 0.001f);  // relativeVelocityX
            Assert.AreEqual(-0.6f, buffer[10], 0.001f); // relativeVelocityZ
        }
    }
}
