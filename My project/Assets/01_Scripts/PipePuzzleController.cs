using System.Collections;
using UnityEngine;

// 新しいInput Systemが有効な場合
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class PipePuzzleController : MonoBehaviour
{
    [Header("カメラ設定")]
    [SerializeField] private Camera mainCamera;

    [Header("回転設定")]
    [SerializeField] private float rotationDuration = 0.25f;

    // 現在、回転アニメーション中かどうか
    private bool isRotating = false;

    // タイルを回転させた合計回数
    private int rotationCount = 0;


    // ゲーム開始時に呼ばれる
    private void Start()
    {
        // カメラが設定されていなければ自動取得
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }
    }


    // 毎フレーム呼ばれる
    private void Update()
    {
        // 回転中はクリック操作を受け付けない
        if (isRotating) return;

        // カメラがなければ処理しない
        if (mainCamera == null) return;

        // 左クリックされたか確認
        if (!GetMouseClick(out Vector2 mousePosition)) return;

        // カメラからマウスの位置にRayを飛ばす
        Ray ray = mainCamera.ScreenPointToRay(mousePosition);

        // Rayが何かに当たったか判定
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            Transform clickedObject = hit.collider.transform;

            // クリックしたオブジェクトから親をたどる
            while (clickedObject != null && clickedObject.parent != transform)
            {
                clickedObject = clickedObject.parent;
            }

            // パズル盤面の子でなければ終了
            if (clickedObject == null) return;

            // タイル以外なら回転させない
            if (!clickedObject.name.StartsWith("Tile_R")) return;

            // タイルの回転アニメーションを開始
            StartCoroutine(RotateTile(clickedObject));
        }
    }


    // マウスの左クリックを取得する
    private bool GetMouseClick(out Vector2 mousePosition)
    {
#if ENABLE_INPUT_SYSTEM

        // 新しいInput System
        if (Mouse.current != null &&
            Mouse.current.leftButton.wasPressedThisFrame)
        {
            mousePosition = Mouse.current.position.ReadValue();
            return true;
        }

#elif ENABLE_LEGACY_INPUT_MANAGER

        // 従来のInput Manager
        if (Input.GetMouseButtonDown(0))
        {
            Vector3 position = Input.mousePosition;

            mousePosition = new Vector2(position.x, position.y);
            return true;
        }

#endif

        mousePosition = Vector2.zero;
        return false;
    }


    // タイルを90度回転させる
    private IEnumerator RotateTile(Transform tile)
    {
        // 回転中にする
        isRotating = true;

        // 回転開始時の角度
        Quaternion startRotation = tile.localRotation;

        // 90度回転した後の角度
        Quaternion targetRotation =
            startRotation * Quaternion.Euler(0f, 90f, 0f);

        // 経過時間
        float elapsedTime = 0f;

        // 指定時間まで回転させる
        while (elapsedTime < rotationDuration)
        {
            elapsedTime += Time.deltaTime;

            // 進行度を0～1で求める
            float t = Mathf.Clamp01(
                elapsedTime / rotationDuration
            );

            // 動きを滑らかにする
            t = Mathf.SmoothStep(0f, 1f, t);

            // 現在の角度を更新
            tile.localRotation = Quaternion.Slerp(startRotation, targetRotation, t);

            // 次のフレームまで待つ
            yield return null;
        }

        // 最終角度を正確に設定
        tile.localRotation = targetRotation;

        // 回転回数を増やす
        rotationCount++;

        // Consoleに回転情報を表示
        Debug.Log($"{tile.name} を回転！ 合計：{rotationCount}回");

        // 回転終了
        isRotating = false;
    }
}