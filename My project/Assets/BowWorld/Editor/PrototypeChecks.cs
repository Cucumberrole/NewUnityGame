using System;
using UnityEditor;
using UnityEngine;

namespace BowWorld.Editor
{
    // NUnitパッケージ不要。UnityのToolsメニューから実行できます。
    public static class PrototypeChecks
    {
        [MenuItem("Tools/Bow World/Run Prototype Checks")]
        public static void Run()
        {
            var round = new RoundState(3, 30f, 1f);
            Check(round.Health == 3 && round.Remaining == 30f, "初期値");
            Check(round.TryDamage() && round.Health == 2, "被弾");
            Check(!round.TryDamage() && round.Health == 2, "連続被弾は無敵で防ぐ");
            round.Tick(1.01f);
            Check(round.TryDamage() && round.Health == 1, "無敵終了");
            round.Tick(1.01f);
            Check(round.TryDamage() && round.Lost && !round.Won, "HP0で失敗");
            round.Tick(100f);
            Check(round.Lost && !round.Won, "失敗後にクリアへ変わらない");
            round.Reset();
            Check(round.Health == 3 && !round.Finished && round.Remaining == 30f, "再挑戦");
            round.Tick(30f);
            Check(round.Won && !round.TryDamage(), "クリア後のダメージ無効");
            round.Reset();
            round.Tick(-1f);
            Check(round.Remaining == 30f, "負の経過時間を無視");
            Check(TiltingArena.IsFloorAt(0f, -7.5f), "中央通路は床あり");
            Check(!TiltingArena.IsFloorAt(-3f, -7.5f), "左の落下口");
            Check(!TiltingArena.IsFloorAt(3f, -7.5f), "右の落下口");
            Check(TiltingArena.IsFloorAt(3f, 0f), "敵レーン奥の床");
            Check(!TiltingArena.IsFloorAt(0f, -10f), "床の外");
            Debug.Log("Bow World: 14 checks passed. Play動作・入力・描画は別途確認してください。");
        }

        private static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("Bow World check failed: " + name);
        }
    }
}
