using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace BoxingAI
{
    /// <summary>
    /// Pure C# Neural Network inference engine for the trained Boxing AI policy.
    ///
    /// Requires ZERO external packages (no Barracuda / Sentis required).
    /// Compatible with all Unity platforms and versions.
    ///
    /// Supports:
    /// - Version 1: Legacy pre-trained binary weights.
    /// - Version 2: Self-describing versioned contract with observation/action dimension,
    ///              combat rules version, and schema FNV-1a hash verification.
    ///
    /// Architecture (matches Python/rl/policy.py):
    ///   Input:      18 floats (normalized observation)
    ///   Layer 0:    Linear(18, 256) + LayerNorm(256) + ReLU
    ///   Layer 1:    Linear(256, 256) + LayerNorm(256) + ReLU
    ///   Layer 2:    Linear(256, 128) + LayerNorm(128) + ReLU
    ///   Actor Head: Linear(128, 12) -> 12 action logits
    ///
    /// Memory: Zero GC allocations during inference (preallocated buffers).
    /// Execution: ~0.02ms on standard CPU.
    /// </summary>
    public class RLPolicyNetwork
    {
        public const uint MAGIC = 0x424F5849; // "BOXI"
        public const uint EXPECTED_SCHEMA_HASH = 0xD6A3BCD8; // FNV-1a 32-bit of canonical 18-obs / 12-act contract string
        public const string CANONICAL_CONTRACT = "obs18:distance,selfHealth01,selfStamina01,opponentHealth01,opponentStamina01,selfGuard01,opponentGuard01,relativeX,relativeZ,relativeVelocityX,relativeVelocityZ,opponentAttackIntent01,incomingAttack,opponentRecovering,selfCanAttack,selfCanDodge,selfCanBlock,healthAdvantage|act12:Idle,MoveForward,MoveBackward,StrafeLeft,StrafeRight,Jab,Cross,HookLeft,HookRight,Block,DodgeLeft,DodgeRight";
        private const float EPS = 1e-5f;

        // Weights & Biases
        private float[] w0, b0; // Layer 0: [256, 18], [256]
        private float[] g1, b1; // LN 0:    [256],     [256]
        private float[] w3, b3; // Layer 1: [256, 256],[256]
        private float[] g4, b4; // LN 1:    [256],     [256]
        private float[] w6, b6; // Layer 2: [128, 256],[128]
        private float[] g7, b7; // LN 2:    [128],     [128]
        private float[] wa, ba; // Actor:   [12, 128], [12]

        // Preallocated evaluation buffers (zero garbage collection)
        private readonly float[] h0 = new float[256];
        private readonly float[] h1 = new float[256];
        private readonly float[] h2 = new float[128];
        private readonly float[] logits = new float[12];

        public bool IsLoaded { get; private set; }
        public uint PolicyVersion { get; private set; } = 1;
        public uint RulesVersion { get; private set; } = 1;
        public uint ObsDim { get; private set; } = 18;
        public uint ActionDim { get; private set; } = 12;
        public float DistanceNorm { get; private set; } = 10.0f;
        public float PosNorm { get; private set; } = 5.0f;

        public static uint ComputeFnv1a32(string text)
        {
            uint hash = 0x811C9DC5;
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= 0x01000193;
            }
            return hash;
        }

        public static RLPolicyNetwork LoadFromBytes(byte[] bytes)
        {
            var net = new RLPolicyNetwork();
            if (bytes == null || bytes.Length == 0)
                throw new ArgumentException("Byte array is null or empty.");

            using (var ms = new MemoryStream(bytes))
            using (var reader = new BinaryReader(ms))
            {
                uint magic = reader.ReadUInt32();
                if (magic != MAGIC)
                    throw new InvalidDataException($"Invalid policy weights header magic: 0x{magic:X8} (expected 0x{MAGIC:X8})");

                uint version = reader.ReadUInt32();
                if (version != 1 && version != 2)
                    throw new InvalidDataException($"Unsupported policy weights version: {version}. Expected version 1 or 2.");

                net.PolicyVersion = version;

                if (version == 2)
                {
                    uint obsDim = reader.ReadUInt32();
                    uint actionDim = reader.ReadUInt32();
                    uint rulesVersion = reader.ReadUInt32();
                    uint schemaHash = reader.ReadUInt32();
                    float distanceNorm = reader.ReadSingle();
                    float posNorm = reader.ReadSingle();

                    if (obsDim != 18)
                        throw new InvalidDataException($"Mismatched observation dimension in policy header: {obsDim} (expected 18).");
                    if (actionDim != 12)
                        throw new InvalidDataException($"Mismatched action dimension in policy header: {actionDim} (expected 12).");
                    if (schemaHash != EXPECTED_SCHEMA_HASH)
                        throw new InvalidDataException($"Mismatched observation/action schema hash: 0x{schemaHash:X8} (expected 0x{EXPECTED_SCHEMA_HASH:X8}). Re-export weights.");

                    net.ObsDim = obsDim;
                    net.ActionDim = actionDim;
                    net.RulesVersion = rulesVersion;
                    net.DistanceNorm = distanceNorm > 0.01f ? distanceNorm : 10.0f;
                    net.PosNorm = posNorm > 0.01f ? posNorm : 5.0f;
                }
                else
                {
                    // Version 1 legacy defaults
                    net.ObsDim = 18;
                    net.ActionDim = 12;
                    net.RulesVersion = 1;
                    net.DistanceNorm = 10.0f;
                    net.PosNorm = 5.0f;
                }

                const int expectedFloats = (256 * 18) + 256 + 256 + 256 +
                                           (256 * 256) + 256 + 256 + 256 +
                                           (128 * 256) + 128 + 128 + 128 +
                                           (12 * 128) + 12;

                long remainingBytes = reader.BaseStream.Length - reader.BaseStream.Position;
                long requiredBytes = expectedFloats * sizeof(float);
                if (remainingBytes != requiredBytes)
                    throw new InvalidDataException($"Corrupted policy weights: expected {requiredBytes} weight bytes, found {remainingBytes}");

                net.w0 = ReadFloatArray(reader, 256 * 18);
                net.b0 = ReadFloatArray(reader, 256);
                net.g1 = ReadFloatArray(reader, 256);
                net.b1 = ReadFloatArray(reader, 256);

                net.w3 = ReadFloatArray(reader, 256 * 256);
                net.b3 = ReadFloatArray(reader, 256);
                net.g4 = ReadFloatArray(reader, 256);
                net.b4 = ReadFloatArray(reader, 256);

                net.w6 = ReadFloatArray(reader, 128 * 256);
                net.b6 = ReadFloatArray(reader, 128);
                net.g7 = ReadFloatArray(reader, 128);
                net.b7 = ReadFloatArray(reader, 128);

                net.wa = ReadFloatArray(reader, 12 * 128);
                net.ba = ReadFloatArray(reader, 12);
            }

            net.IsLoaded = true;
            return net;
        }

        public BoxingAction Predict(float[] obs, out float[] outLogits)
        {
            if (!IsLoaded)
                throw new InvalidOperationException("RLPolicyNetwork is not loaded.");

            if (obs == null || obs.Length < 18)
                throw new ArgumentException($"Observation input size must be at least 18, got {(obs == null ? 0 : obs.Length)}");

            // Layer 0: Linear (18 -> 256) + LayerNorm + ReLU
            Linear(obs, w0, b0, h0, 256, 18);
            LayerNorm(h0, g1, b1, 256);
            ReLU(h0, 256);

            // Layer 1: Linear (256 -> 256) + LayerNorm + ReLU
            Linear(h0, w3, b3, h1, 256, 256);
            LayerNorm(h1, g4, b4, 256);
            ReLU(h1, 256);

            // Layer 2: Linear (256 -> 128) + LayerNorm + ReLU
            Linear(h1, w6, b6, h2, 128, 256);
            LayerNorm(h2, g7, b7, 128);
            ReLU(h2, 128);

            // Actor Head: Linear (128 -> 12)
            Linear(h2, wa, ba, logits, 12, 128);

            // Argmax
            int bestAction = 0;
            float maxLogit = float.NegativeInfinity;
            for (int i = 0; i < 12; i++)
            {
                if (logits[i] > maxLogit)
                {
                    maxLogit = logits[i];
                    bestAction = i;
                }
            }

            outLogits = logits;
            return (BoxingAction)bestAction;
        }

        public BoxingAction Predict(float[] obs)
        {
            return Predict(obs, out _);
        }

        // --- Low-level Math Kernels ---

        private static void Linear(float[] input, float[] weights, float[] bias, float[] output, int outDim, int inDim)
        {
            for (int i = 0; i < outDim; i++)
            {
                float sum = bias[i];
                int rowOffset = i * inDim;
                for (int j = 0; j < inDim; j++)
                {
                    sum += weights[rowOffset + j] * input[j];
                }
                output[i] = sum;
            }
        }

        private static void LayerNorm(float[] x, float[] gamma, float[] beta, int dim)
        {
            // Compute mean
            float sum = 0f;
            for (int i = 0; i < dim; i++)
                sum += x[i];
            float mean = sum / dim;

            // Compute variance
            float varSum = 0f;
            for (int i = 0; i < dim; i++)
                varSum += (x[i] - mean) * (x[i] - mean);
            float invStd = 1.0f / (float)Math.Sqrt((varSum / dim) + EPS);

            // Apply normalization, scale and shift
            for (int i = 0; i < dim; i++)
            {
                x[i] = ((x[i] - mean) * invStd) * gamma[i] + beta[i];
            }
        }

        private static void ReLU(float[] x, int dim)
        {
            for (int i = 0; i < dim; i++)
            {
                if (x[i] < 0f)
                    x[i] = 0f;
            }
        }

        private static float[] ReadFloatArray(BinaryReader reader, int count)
        {
            byte[] rawBytes = reader.ReadBytes(count * sizeof(float));
            if (rawBytes.Length < count * sizeof(float))
                throw new EndOfStreamException($"Expected {count * sizeof(float)} bytes, but only read {rawBytes.Length}");
            float[] result = new float[count];
            Buffer.BlockCopy(rawBytes, 0, result, 0, rawBytes.Length);
            return result;
        }
    }
}
