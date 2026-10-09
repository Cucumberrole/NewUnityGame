using UnityEngine;

namespace BowWorld
{
    public sealed class BowController : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float bowSeconds = 1f;
        [SerializeField, Min(0.1f)] private float returnSeconds = 0.4f;
        [SerializeField, Range(0f, 85f)] private float cameraBowAngle = 70f;

        private Camera view;
        private Transform arena;
        private GameManager game;
        private readonly Vector3 anchor = new Vector3(0f, 0f, -8f);
        public float Amount { get; private set; }

        public void Initialize(Camera camera, Transform arenaTransform, GameManager manager)
        {
            view = camera;
            arena = arenaTransform;
            game = manager;
        }

        private void Update()
        {
            if (game == null || !game.IsPlaying) return;
            bool held = PrototypeInput.BowHeld;
            float seconds = held ? bowSeconds : returnSeconds;
            Amount = Mathf.MoveTowards(Amount, held ? 1f : 0f,
                Time.deltaTime / Mathf.Max(0.1f, seconds));
        }

        private void LateUpdate()
        {
            if (view == null || arena == null) return;
            // プレイヤーは床上の同じ位置に固定。床の回転を視点へ重ねず、顔だけを下げます。
            view.transform.position = arena.TransformPoint(anchor) + Vector3.up * 1.7f;
            view.transform.rotation = Quaternion.Euler(Amount * cameraBowAngle, 0f, 0f);
        }

        public void ResetBow()
        {
            Amount = 0f;
        }
    }
}
