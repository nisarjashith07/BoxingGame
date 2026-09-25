using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace BoxingAI
{
    /// <summary>
    /// Localhost UDP request/reply bridge between a Gymnasium client and the
    /// running Unity match. All Unity state access stays on Unity's main thread.
    /// </summary>
    public sealed class RLTrainingBridge : MonoBehaviour
    {
        [Serializable]
        public sealed class TrainingCommand
        {
            public string id;
            public string command;
            public int action;
        }

        [Serializable]
        private sealed class TrainingReply
        {
            public string id;
            public string status;
            public BoxingObservation observation;
            public float reward;
            public bool terminated;
            public bool truncated;
            public string winner;
        }

        [SerializeField] private int port = 5056;
        private UdpClient socket;
        private IPEndPoint client;
        private string currentId;
        private string cachedId;
        private string cachedReply;

        public int Port => port;

        private void OnEnable()
        {
            try
            {
                socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
                socket.Client.Blocking = false;
                Debug.Log($"[RL] Unity Gym bridge listening on 127.0.0.1:{port}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[RL] Could not bind UDP port {port}: {e.Message}");
            }
        }

        /// <summary>Polls one Python request. Call from BoxingEnvironment.Update.</summary>
        public bool TryReceive(out TrainingCommand command)
        {
            command = null;
            if (socket == null || socket.Available <= 0) return false;

            try
            {
                var endpoint = new IPEndPoint(IPAddress.Any, 0);
                byte[] bytes = socket.Receive(ref endpoint);
                TrainingCommand received = JsonUtility.FromJson<TrainingCommand>(Encoding.UTF8.GetString(bytes));
                if (received == null || string.IsNullOrEmpty(received.id)) return false;

                client = endpoint;
                if (received.id == cachedId && !string.IsNullOrEmpty(cachedReply))
                {
                    SendRaw(cachedReply);
                    return false;
                }

                // The Python client waits for each action response before sending the next.
                if (received.id == currentId) return false;
                currentId = received.id;
                command = received;
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RL] Ignoring invalid training request: {e.Message}");
                return false;
            }
        }

        public void ReplyReset(BoxingObservation observation)
        {
            Send(new TrainingReply { id = currentId, status = "ok", observation = observation });
        }

        public void ReplyStep(BoxingObservation observation, float reward, bool terminated, bool truncated, string winner)
        {
            Send(new TrainingReply
            {
                id = currentId,
                status = "ok",
                observation = observation,
                reward = reward,
                terminated = terminated,
                truncated = truncated,
                winner = winner ?? string.Empty
            });
        }

        public void ReplyError(string message)
        {
            Send(new TrainingReply { id = currentId, status = "error", winner = message });
        }

        private void Send(TrainingReply reply)
        {
            if (string.IsNullOrEmpty(currentId)) return;
            string json = JsonUtility.ToJson(reply);
            cachedId = currentId;
            cachedReply = json;
            SendRaw(json);
            currentId = null;
        }

        private void SendRaw(string json)
        {
            if (socket == null || client == null || string.IsNullOrEmpty(json)) return;
            try { socket.Send(Encoding.UTF8.GetBytes(json), Encoding.UTF8.GetByteCount(json), client); }
            catch (Exception e) { Debug.LogWarning($"[RL] Could not reply to trainer: {e.Message}"); }
        }

        private void OnDisable()
        {
            socket?.Close();
            socket = null;
        }
    }
}
