using System;

namespace BowWorld
{
    // Unityに依存しないラウンド状態。HUDや出現処理はGameManagerが担当します。
    public sealed class RoundState
    {
        private readonly int initialHealth;
        private readonly float duration;
        private readonly float invulnerability;
        private float invulnerableRemaining;

        public int Health { get; private set; }
        public float Remaining { get; private set; }
        public bool Won { get; private set; }
        public bool Lost { get; private set; }
        public bool Finished => Won || Lost;
        public bool Invulnerable => invulnerableRemaining > 0f;

        public RoundState(int health, float seconds, float invulnerabilitySeconds)
        {
            initialHealth = Math.Max(1, health);
            duration = Math.Max(0.1f, seconds);
            invulnerability = Math.Max(0f, invulnerabilitySeconds);
            Reset();
        }

        public void Reset()
        {
            Health = initialHealth;
            Remaining = duration;
            invulnerableRemaining = 0f;
            Won = false;
            Lost = false;
        }

        public void Tick(float deltaTime)
        {
            if (Finished || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            float dt = Math.Max(0f, deltaTime);
            invulnerableRemaining = Math.Max(0f, invulnerableRemaining - dt);
            Remaining = Math.Max(0f, Remaining - dt);
            if (Remaining <= 0f) Won = true;
        }

        public bool TryDamage()
        {
            if (Finished || Invulnerable) return false;
            Health = Math.Max(0, Health - 1);
            invulnerableRemaining = invulnerability;
            if (Health == 0) Lost = true;
            return true;
        }
    }
}
