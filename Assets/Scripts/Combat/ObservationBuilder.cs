using UnityEngine;

namespace BoxingAI
{
    /// <summary>
    /// Builds a BoxingObservation struct from live game state.
    /// Called each frame by BoxingEnvironment to feed the AI brain.
    /// </summary>
    public static class ObservationBuilder
    {
        public static BoxingObservation Build(
            FighterState aiState,
            FighterState playerState,
            PunchDetector playerDetector,
            AIFighterController aiController,
            float roundTime01)
        {
            Vector3 aiPos = aiState.position;
            Vector3 playerPos = playerState.position;
            Vector3 delta = playerPos - aiPos;
            Vector3 relativeVelocity = playerState.velocity - aiState.velocity;

            Vector3 localDelta = aiController != null ? aiController.transform.InverseTransformDirection(delta) : delta;
            Vector3 localRelVel = aiController != null ? aiController.transform.InverseTransformDirection(relativeVelocity) : relativeVelocity;

            return new BoxingObservation
            {
                distance = Vector3.Distance(aiPos, playerPos),
                selfHealth01 = aiState.Health01,
                selfStamina01 = aiState.Stamina01,
                opponentHealth01 = playerState.Health01,
                opponentStamina01 = playerState.Stamina01,
                selfGuard01 = aiState.guard,
                opponentGuard01 = playerState.guard,
                relativeX = localDelta.x,
                relativeZ = localDelta.z,
                relativeVelocityX = localRelVel.x,
                relativeVelocityZ = localRelVel.z,
                opponentAttackIntent01 = playerDetector.IsPunching ? 1f : 0f,
                incomingAttack = playerDetector.IsPunching,
                opponentRecovering = playerDetector.IsRecovering,
                selfCanAttack = aiState.CanAttack,
                selfCanDodge = aiState.CanDodge,
                selfCanBlock = aiState.CanBlock,
                roundTime01 = roundTime01
            };
        }
    }
}
