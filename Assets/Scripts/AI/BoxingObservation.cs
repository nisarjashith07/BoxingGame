using System;

namespace BoxingAI
{
    [Serializable]
    public struct BoxingObservation
    {
        public float distance;
        public float selfHealth01;
        public float selfStamina01;
        public float opponentHealth01;
        public float opponentStamina01;
        public float selfGuard01;
        public float opponentGuard01;

        public float relativeX;
        public float relativeZ;
        public float relativeVelocityX;
        public float relativeVelocityZ;

        public float opponentAttackIntent01;
        public bool incomingAttack;
        public bool opponentRecovering;
        public bool selfCanAttack;
        public bool selfCanDodge;
        public bool selfCanBlock;
        public float roundTime01;
    }
}
