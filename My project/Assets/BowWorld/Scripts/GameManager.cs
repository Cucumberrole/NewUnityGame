using System.Collections.Generic;
using UnityEngine;

namespace BowWorld
{
    public sealed class GameManager : MonoBehaviour
    {
        [SerializeField, Min(1)] private int startHealth = 3;
        [SerializeField, Min(5f)] private float roundSeconds = 30f;
        [SerializeField, Min(0.6f)] private float enemySpawnInterval = 2.8f;
        [SerializeField, Min(1f)] private float barrelSpawnInterval = 4.8f;

        private RoundState round;
        private BowController bow;
        private TiltingArena arena;
        private Material enemyMaterial;
        private Material barrelMaterial;
        private readonly List<ArenaActor> actors = new List<ArenaActor>();
        private AudioSource sound;
        private AudioClip enemyCue;
        private AudioClip barrelCue;
        private AudioClip hitCue;
        private float nextEnemy;
        private float nextBarrel;
        private float damageFlash;
        private int enemyCount;
        private int fallenCount;
        private GUIStyle largeText;
        private GUIStyle smallText;

        public bool IsPlaying => round != null && !round.Finished;

        public void Initialize(BowController controller, TiltingArena floor,
            Material enemyAppearance, Material barrelAppearance,
            float duration, float enemyInterval, float barrelInterval)
        {
            bow = controller;
            arena = floor;
            enemyMaterial = enemyAppearance;
            barrelMaterial = barrelAppearance;
            roundSeconds = Mathf.Max(5f, duration);
            enemySpawnInterval = Mathf.Max(0.6f, enemyInterval);
            barrelSpawnInterval = Mathf.Max(1f, barrelInterval);
            round = new RoundState(startHealth, roundSeconds, 1f);
            sound = gameObject.AddComponent<AudioSource>();
            sound.playOnAwake = false;
            sound.spatialBlend = 0f;
            sound.volume = 0.22f;
            enemyCue = MakeTone("Enemy: low", 260f, 0.16f);
            barrelCue = MakeTone("Barrel: high", 880f, 0.24f);
            hitCue = MakeTone("Damage", 100f, 0.15f);
            RestartRound();
        }

        private void Update()
        {
            if (round == null) return;
            if (PrototypeInput.RestartPressed)
            {
                RestartRound();
                return;
            }
            damageFlash = Mathf.Max(0f, damageFlash - Time.deltaTime);
            if (!IsPlaying) return;
            round.Tick(Time.deltaTime);
            if (!IsPlaying) return;

            nextEnemy -= Time.deltaTime;
            nextBarrel -= Time.deltaTime;
            if (nextEnemy <= 0f)
            {
                SpawnEnemy();
                nextEnemy = enemySpawnInterval;
            }
            if (nextBarrel <= 0f)
            {
                SpawnBarrel();
                nextBarrel = barrelSpawnInterval;
            }
        }

        private void SpawnEnemy()
        {
            float lane = enemyCount++ % 2 == 0 ? -3f : 3f;
            GameObject obj = MakeActor(PrimitiveType.Capsule, "Enemy", enemyMaterial);
            obj.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
            var actor = obj.AddComponent<EnemyController>();
            actor.Initialize(this, arena, new Vector3(lane, 0.8f, 7.2f));
            actors.Add(actor);
            sound.PlayOneShot(enemyCue);
        }

        private void SpawnBarrel()
        {
            GameObject obj = MakeActor(PrimitiveType.Cylinder, "Barrel", barrelMaterial);
            obj.transform.localScale = new Vector3(1.1f, 0.48f, 1.1f);
            var actor = obj.AddComponent<BarrelController>();
            actor.Initialize(this, arena, new Vector3(0f, 0.55f, 3f));
            actors.Add(actor);
            sound.PlayOneShot(barrelCue);
        }

        private GameObject MakeActor(PrimitiveType type, string actorName, Material material)
        {
            GameObject obj = GameObject.CreatePrimitive(type);
            obj.name = actorName;
            // 共通ルートの子にして、Play終了時に確実に片付けます。
            // arenaの子にはしません。落下後は床と独立して動かすためです。
            obj.transform.SetParent(transform, false);
            obj.GetComponent<Renderer>().sharedMaterial = material;
            Collider collider = obj.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                Destroy(collider);
            }
            return obj;
        }

        public void TryDamage()
        {
            if (round == null || !round.TryDamage()) return;
            damageFlash = 0.35f;
            sound.PlayOneShot(hitCue);
        }

        public void Unregister(ArenaActor actor) => actors.Remove(actor);
        public void ActorFell() => fallenCount++;

        public void RestartRound()
        {
            // Destroyはフレーム末尾なので、先に無効化して同フレームの被弾を防ぎます。
            foreach (ArenaActor actor in actors)
            {
                if (actor == null) continue;
                actor.gameObject.SetActive(false);
                Destroy(actor.gameObject);
            }
            actors.Clear();
            round.Reset();
            bow.ResetBow();
            arena.ResetTilt();
            nextEnemy = 1.2f;
            nextBarrel = 3.5f;
            damageFlash = 0f;
            enemyCount = 0;
            fallenCount = 0;
            sound.Stop();
        }

        private static AudioClip MakeTone(string clipName, float frequency, float seconds)
        {
            const int rate = 22050;
            int count = Mathf.CeilToInt(rate * seconds);
            float[] samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / rate;
                // 両端を滑らかにしてクリックノイズを抑えます。
                float envelope = Mathf.Sin(Mathf.PI * i / (count - 1));
                samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * 0.5f;
            }
            AudioClip clip = AudioClip.Create(clipName, count, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void OnGUI()
        {
            if (round == null) return;
            // 英語のHUDで、追加の日本語フォントなしでも表示できるようにしています。
            if (largeText == null)
            {
                largeText = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 26,
                    alignment = TextAnchor.MiddleCenter
                };
                smallText = new GUIStyle(GUI.skin.label) { fontSize = 17 };
                largeText.normal.textColor = Color.white;
                smallText.normal.textColor = Color.white;
            }
            Matrix4x4 previous = GUI.matrix;
            Color previousColor = GUI.color;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity,
                new Vector3(Screen.width / 1280f, Screen.height / 720f, 1f));
            GUI.Box(new Rect(12f, 12f, 400f, 132f), GUIContent.none);
            GUI.Label(new Rect(24f, 20f, 360f, 30f),
                "HP: " + round.Health + " / " + startHealth +
                "    TIME: " + round.Remaining.ToString("F1"), smallText);
            GUI.Label(new Rect(24f, 52f, 360f, 26f),
                "BOW: " + Mathf.RoundToInt(bow.Amount * 100f) + "%   FALLEN: " + fallenCount, smallText);
            GUI.Label(new Rect(24f, 82f, 360f, 26f), "SPACE: bow / release: look up", smallText);
            GUI.Label(new Rect(24f, 109f, 360f, 26f), "R: restart    Low tone: enemy / High: barrel", smallText);
            GUI.Label(new Rect(250f, 658f, 780f, 40f),
                "Deep bow drops enemies. Look up to slow the barrels.", largeText);

            if (damageFlash > 0f)
            {
                GUI.color = new Color(1f, 0.1f, 0.1f, damageFlash * 0.55f);
                GUI.DrawTexture(new Rect(0f, 0f, 1280f, 720f), Texture2D.whiteTexture);
                GUI.color = previousColor;
            }
            if (round.Finished)
            {
                GUI.Box(new Rect(365f, 250f, 550f, 170f), GUIContent.none);
                GUI.Label(new Rect(385f, 270f, 510f, 60f),
                    round.Won ? "CLEAR!" : "GAME OVER", largeText);
                GUI.Label(new Rect(385f, 345f, 510f, 40f), "Press R to try again", largeText);
            }
            GUI.matrix = previous;
            GUI.color = previousColor;
        }

        private void OnDestroy()
        {
            if (enemyCue != null) Destroy(enemyCue);
            if (barrelCue != null) Destroy(barrelCue);
            if (hitCue != null) Destroy(hitCue);
        }
    }
}
