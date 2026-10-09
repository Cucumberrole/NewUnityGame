using UnityEngine;

namespace BowWorld
{
    // 床の上はローカル座標、落下後はワールド座標で動かす共通処理です。
    public abstract class ArenaActor : MonoBehaviour
    {
        protected GameManager Game;
        protected TiltingArena Arena;
        protected Vector3 LocalPosition;
        protected bool Falling;
        protected bool Removed;
        protected float Lifetime;
        private float fallSeconds;
        private Vector3 fallVelocity;

        public virtual void Initialize(GameManager game, TiltingArena arena, Vector3 localPosition)
        {
            Game = game;
            Arena = arena;
            LocalPosition = localPosition;
            AlignToFloor();
        }

        protected virtual void Update()
        {
            if (Game == null || Arena == null || !Game.IsPlaying || Removed) return;
            float dt = Time.deltaTime;
            Lifetime += dt;
            if (Falling)
            {
                fallSeconds += dt;
                fallVelocity += Vector3.down * 16f * dt;
                transform.position += fallVelocity * dt;
                transform.Rotate(70f * dt, 30f * dt, 0f, Space.Self);
                if (fallSeconds > 2f) Remove();
                return;
            }
            StepOnFloor(dt);
            if (Removed || Falling) return;
            if (!TiltingArena.IsFloorAt(LocalPosition.x, LocalPosition.z))
            {
                AlignToFloor();
                Falling = true;
                fallVelocity = Arena.transform.TransformDirection(Vector3.back * 2f);
                Game.ActorFell();
                return;
            }
            AlignToFloor();
            if (Lifetime > 16f) Remove();
        }

        protected abstract void StepOnFloor(float dt);

        protected virtual void AlignToFloor()
        {
            transform.position = Arena.WorldPosition(LocalPosition);
            transform.rotation = Arena.transform.rotation;
        }

        protected void Remove()
        {
            if (Removed) return;
            Removed = true;
            Game.Unregister(this);
            gameObject.SetActive(false);
            Destroy(gameObject);
        }
    }
}
