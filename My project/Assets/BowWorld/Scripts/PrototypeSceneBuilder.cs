using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BowWorld
{
    // 空のGameObjectへこのコンポーネントだけを付け、Playでシーンを生成します。
    [DisallowMultipleComponent]
    public sealed class PrototypeSceneBuilder : MonoBehaviour
    {
        [SerializeField, Min(5f)] private float roundSeconds = 30f;
        [SerializeField, Min(0.6f)] private float enemySpawnInterval = 2.8f;
        [SerializeField, Min(1f)] private float barrelSpawnInterval = 4.8f;
        private GameObject generatedRoot;
        private readonly List<Material> materials = new List<Material>();
        private readonly List<Camera> oldCameras = new List<Camera>();
        private readonly List<AudioListener> oldListeners = new List<AudioListener>();

        private void Start()
        {
            if (FindObjectsByType<PrototypeSceneBuilder>(FindObjectsSortMode.None).Length > 1)
            {
                Debug.LogError("Bow World: PrototypeSceneBuilderはシーンに1個だけ配置してください。", this);
                enabled = false;
                return;
            }

            // Play中だけ既存のカメラを止めます。コンポーネントを破棄すると元へ戻します。
            foreach (Camera existing in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (!existing.enabled) continue;
                oldCameras.Add(existing);
                existing.enabled = false;
            }
            foreach (AudioListener existing in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            {
                if (!existing.enabled) continue;
                oldListeners.Add(existing);
                existing.enabled = false;
            }

            generatedRoot = new GameObject("Bow World - Generated");
            generatedRoot.transform.SetParent(transform, false);
            generatedRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            generatedRoot.transform.localScale = Vector3.one;
            var manager = generatedRoot.AddComponent<GameManager>();
            var bow = generatedRoot.AddComponent<BowController>();

            Material floor = MakeMaterial(new Color(0.12f, 0.14f, 0.2f));
            Material lane = MakeMaterial(new Color(0.06f, 0.35f, 0.4f));
            Material neon = MakeMaterial(new Color(0.05f, 0.8f, 0.9f));
            Material danger = MakeMaterial(new Color(0.85f, 0.12f, 0.27f));
            Material barrel = MakeMaterial(new Color(1f, 0.6f, 0.08f));
            Material player = MakeMaterial(new Color(0.16f, 0.85f, 0.32f));

            GameObject arenaObj = new GameObject("Tilting Arena");
            arenaObj.transform.SetParent(generatedRoot.transform, false);
            var rigidbody = arenaObj.AddComponent<Rigidbody>();
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
            var arena = arenaObj.AddComponent<TiltingArena>();

            // 奥の大床：-6 <= Z <= 9。手前には中央の細い通路だけを接続します。
            Box("Main Floor", arenaObj.transform, new Vector3(0f, -0.2f, 1.5f),
                new Vector3(12f, 0.4f, 15f), floor, true);
            Box("Center Bridge", arenaObj.transform, new Vector3(0f, -0.2f, -7.5f),
                new Vector3(2f, 0.4f, 3f), floor, true);

            // 線は床面より少し上。左右の穴には床を作りません。
            foreach (float x in new[] { -3f, 3f })
            {
                Box("Enemy Lane", arenaObj.transform, new Vector3(x, 0.008f, 1.5f),
                    new Vector3(0.08f, 0.012f, 15f), lane, false);
                Box("Fall Edge", arenaObj.transform, new Vector3(x, 0.018f, -5.98f),
                    new Vector3(3f, 0.024f, 0.1f), danger, false);
            }
            Box("Barrel Lane", arenaObj.transform, new Vector3(0f, 0.008f, 0f),
                new Vector3(0.08f, 0.012f, 17.8f), neon, false);
            Box("Player Anchor", arenaObj.transform, new Vector3(0f, 0.04f, -8f),
                new Vector3(0.8f, 0.08f, 0.8f), player, false);

            // 壁は置かず、床の輪郭と柱で傾きを見せます。
            for (int z = -4; z <= 8; z += 4)
            {
                foreach (float x in new[] { -5.8f, 5.8f })
                    Box("Neon Pillar", arenaObj.transform, new Vector3(x, 1.5f, z),
                        new Vector3(0.14f, 3f, 0.14f), neon, false);
            }

            GameObject cameraObj = new GameObject("Bow Camera");
            cameraObj.transform.SetParent(generatedRoot.transform, false);
            cameraObj.tag = "MainCamera";
            Camera camera = cameraObj.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.025f, 0.06f);
            camera.fieldOfView = 75f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 100f;
            cameraObj.AddComponent<AudioListener>();

            GameObject lightObj = new GameObject("Prototype Light");
            lightObj.transform.SetParent(generatedRoot.transform, false);
            lightObj.transform.rotation = Quaternion.Euler(45f, -25f, 0f);
            Light light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;

            bow.Initialize(camera, arenaObj.transform, manager);
            arena.Initialize(bow, manager);
            manager.Initialize(bow, arena, danger, barrel,
                roundSeconds, enemySpawnInterval, barrelSpawnInterval);
        }

        private Material MakeMaterial(Color color)
        {
            // URPとBuilt-inで使えるUnlitを選び、照明や追加素材に依存しない見た目にします。
            Shader shader = GraphicsSettings.currentRenderPipeline != null
                ? Shader.Find("Universal Render Pipeline/Unlit") : Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null)
                throw new System.InvalidOperationException("Bow World: Unlitシェーダーが見つかりません。");
            Material material = new Material(shader);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            materials.Add(material);
            return material;
        }

        private static GameObject Box(string objectName, Transform parent,
            Vector3 position, Vector3 scale, Material material, bool hasCollider)
        {
            GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = objectName;
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position;
            obj.transform.localScale = scale;
            obj.GetComponent<Renderer>().sharedMaterial = material;
            if (!hasCollider)
            {
                Collider collider = obj.GetComponent<Collider>();
                collider.enabled = false;
                Destroy(collider);
            }
            return obj;
        }

        private void OnDestroy()
        {
            if (generatedRoot != null) Destroy(generatedRoot);
            foreach (Material material in materials)
                if (material != null) Destroy(material);
            foreach (Camera existing in oldCameras)
                if (existing != null) existing.enabled = true;
            foreach (AudioListener existing in oldListeners)
                if (existing != null) existing.enabled = true;
        }
    }
}
