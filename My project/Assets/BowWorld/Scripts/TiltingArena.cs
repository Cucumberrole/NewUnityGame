using UnityEngine;

namespace BowWorld
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class TiltingArena : MonoBehaviour
    {
        [SerializeField, Range(0f, 40f)] private float maxTiltAngle = 30f;
        private Rigidbody body;
        private BowController bow;
        private GameManager game;
        public float Angle { get; private set; }

        public void Initialize(BowController controller, GameManager manager)
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            bow = controller;
            game = manager;
        }

        private void FixedUpdate()
        {
            if (body == null || bow == null || game == null || !game.IsPlaying) return;
            Angle = maxTiltAngle * bow.Amount;
            // -X回転で奥（+Z）が上がり、手前（-Z）が下がります。
            body.MoveRotation(Quaternion.Euler(-Angle, 0f, 0f));
        }

        public void ResetTilt()
        {
            Angle = 0f;
            if (body != null) body.rotation = Quaternion.identity;
            transform.rotation = Quaternion.identity;
        }

        public Vector3 WorldPosition(Vector3 localPosition) => transform.TransformPoint(localPosition);

        // SceneBuilderの床形状と同じ範囲。手前左右を欠かせ、中央幅2だけ残します。
        public static bool IsFloorAt(float x, float z)
        {
            if (Mathf.Abs(x) > 6f || z < -9f || z > 9f) return false;
            return z >= -6f || Mathf.Abs(x) <= 1f;
        }
    }
}
