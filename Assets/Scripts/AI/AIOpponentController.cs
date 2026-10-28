using UnityEngine;

namespace BoxingAI
{
    public class AIOpponentController : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private AIOpponentProfile profile;
        [SerializeField] private AIDifficulty difficulty = AIDifficulty.Easy;
        [SerializeField] private bool createRuntimeProfile = true;

        [Header("Debug")]
        [SerializeField] private bool logActions = false;

        private AIOpponentBrain brain;
        private BoxingObservation latestObservation;
        private BoxingAction currentAction = BoxingAction.Idle;

        public BoxingAction CurrentAction => currentAction;
        public BoxingObservation LatestObservation => latestObservation;
        public AIOpponentBrain Brain => brain;

        private void Awake()
        {
            if (createRuntimeProfile || profile == null)
                profile = AIOpponentProfile.CreateRuntime(difficulty);

            brain = new AIOpponentBrain(profile);
        }

        public void SetDifficulty(AIDifficulty newDifficulty)
        {
            difficulty = newDifficulty;
            profile = AIOpponentProfile.CreateRuntime(difficulty);
            brain = new AIOpponentBrain(profile);
        }

        public BoxingAction Evaluate(BoxingObservation observation)
        {
            latestObservation = observation;
            currentAction = brain.Decide(observation, Time.deltaTime);

            if (logActions)
                Debug.Log($"[AI {difficulty}] Action: {currentAction}");

            return currentAction;
        }

        public void ResetBrain()
        {
            brain = new AIOpponentBrain(profile);
            currentAction = BoxingAction.Idle;
            latestObservation = default;
        }
    }
}
