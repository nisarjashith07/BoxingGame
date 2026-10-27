using System;
using System.IO;
using UnityEngine;

namespace BoxingAI
{
    /// <summary>
    /// Pure C# Neural Network inference engine for the trained Boxing AI policy.
    ///
    /// Requires ZERO external packages (no Barracuda / Sentis required).
    /// Compatible with all Unity platforms and versions.
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
        private const uint MAGIC = 0x424F5849; // "BOXI"
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
                    throw new InvalidDataException($"Invalid policy weights header magic: 0x{magic:X8}");

                uint version = reader.ReadUInt32();

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

            if (obs.Length < 18)
                throw new ArgumentException($"Observation input size must be at least 18, got {obs.Length}");

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
            {
                float diff = x[i] - mean;
                varSum += diff * diff;
            }
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
            float[] result = new float[count];
            Buffer.BlockCopy(rawBytes, 0, result, 0, rawBytes.Length);
            return result;
        }
    }
}
