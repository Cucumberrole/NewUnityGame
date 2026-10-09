using UnityEngine;

namespace BowWorld
{
    public sealed class EnemyController : ArenaActor
    {
        [SerializeField, Min(0f)] private float walkSpeed = 2.1f;
        [SerializeField, Range(0f, 30f)] private float slipAngle = 21f;
        [SerializeField, Min(0.1f)] private float attackDelay = 1.1f;
        private float attackRemaining;
        private Renderer appearance;
        private Color originalColor;

        public override void Initialize(GameManager game, TiltingArena arena, Vector3 localPosition)
        {
            base.Initialize(game, arena, localPosition);
            attackRemaining = attackDelay;
            appearance = GetComponent<Renderer>();
            if (appearance != null) originalColor = appearance.sharedMaterial.color;
        }

        protected override void StepOnFloor(float dt)
        {
            if (Arena.Angle >= slipAngle)
            {
                // 深いおじぎで踏ん張れなくなり、そのまま落下口へ滑ります。
                LocalPosition.z -= (3f + Arena.Angle * 0.16f) * dt;
                attackRemaining = attackDelay;
                SetWarning(false);
                return;
            }

            LocalPosition.z = Mathf.Max(-5.6f, LocalPosition.z - walkSpeed * dt);
            if (LocalPosition.z > -5.59f) return;
            // 浅いおじぎでは穴の手前で止まり、予告してから主人公へ攻撃します。
            attackRemaining -= dt;
            SetWarning(true);
            if (attackRemaining <= 0f)
            {
                Game.TryDamage();
                Remove();
            }
        }

        private void SetWarning(bool enabled)
        {
            if (appearance == null) return;
            var block = new MaterialPropertyBlock();
            Color color = enabled && Mathf.Sin(Lifetime * 18f) > 0f ? Color.white : originalColor;
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            appearance.SetPropertyBlock(block);
        }
    }
}
