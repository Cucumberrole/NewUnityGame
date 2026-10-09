using UnityEngine;

namespace BowWorld
{
    public sealed class BarrelController : ArenaActor
    {
        [SerializeField, Min(0f)] private float speed = 3.2f;
        [SerializeField, Min(0f)] private float rollingResistance = 3f;
        private float rollAngle;
        private float stoppedSeconds;

        protected override void StepOnFloor(float dt)
        {
            // 傾きによる加速から抵抗を引く簡易モデルです。平らに戻すと減速します。
            float acceleration = 12f * Mathf.Sin(Arena.Angle * Mathf.Deg2Rad) - rollingResistance;
            speed = Mathf.Clamp(speed + acceleration * dt, 0f, 11f);
            LocalPosition.z -= speed * dt;
            rollAngle -= speed * dt * Mathf.Rad2Deg / 0.55f;

            if (LocalPosition.z <= -7.25f)
            {
                Game.TryDamage();
                Remove();
                return;
            }
            stoppedSeconds = speed < 0.05f ? stoppedSeconds + dt : 0f;
            if (stoppedSeconds > 0.8f) Remove();
        }

        protected override void AlignToFloor()
        {
            transform.position = Arena.WorldPosition(LocalPosition);
            // シリンダーの軸を左右へ向け、床に沿って回転させます。
            transform.rotation = Arena.transform.rotation * Quaternion.Euler(rollAngle, 0f, 90f);
        }
    }
}
