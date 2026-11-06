using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class PipePuzzleController : MonoBehaviour
{
    [Header("カメラ設定")]
    [SerializeField] private Camera mainCamera;

    [Header("回転設定")]
    [SerializeField] private float rotationDuration = 0.25f;

    [Header("植物の成長設定")]
    [SerializeField, Min(1)] private int growthPerRotation = 10;
    [SerializeField] private GameObject[] plantStages = new GameObject[4];

    private bool isRotating = false;
    private bool isClear = false;
    private bool boardReady = false;
    private int rotationCount = 0;
    private int plantGrowth = 0;
    private const int GridSize = 3;

    private enum PipeType { Straight, Corner }

    [System.Flags]
    private enum Direction { None = 0, North = 1, East = 2, South = 4, West = 8 }

    private readonly Transform[,] tiles = new Transform[GridSize, GridSize];
    private readonly PipeType[,] pipeTypes = new PipeType[GridSize, GridSize];

    // ゲーム開始時
    private void Start()
    {
        if (mainCamera == null) mainCamera = Camera.main;

        // 植物を初期状態にする
        UpdatePlantVisual();

        boardReady = LoadTiles();
        if (boardReady) CheckPuzzleClear();
    }

    // タイル情報を読み込む
    private bool LoadTiles()
    {
        for (int row = 0; row < GridSize; row++)
        {
            for (int col = 0; col < GridSize; col++)
            {
                string tileName = $"Tile_R{row + 1}_C{col + 1}";
                Transform tile = transform.Find(tileName);

                if (tile == null)
                {
                    Debug.LogError($"{tileName} が見つかりません！");
                    return false;
                }

                tiles[row, col] = tile;
                bool straight = false, horizontal = false, vertical = false;

                foreach (Transform child in tile.GetComponentsInChildren<Transform>(true))
                {
                    if (child == tile) continue;

                    string name = child.name.Trim();
                    if (name.StartsWith("Pipe_Straight")) straight = true;
                    if (name.StartsWith("Pipe_Horizontal")) horizontal = true;
                    if (name.StartsWith("Pipe_Vertical") || name.StartsWith("Pipe_Vartical")) vertical = true;
                }

                if (horizontal && vertical && !straight) pipeTypes[row, col] = PipeType.Corner;
                else if (straight && !horizontal && !vertical) pipeTypes[row, col] = PipeType.Straight;
                else
                {
                    Debug.LogError($"{tileName} のパイプが判別できません！");
                    return false;
                }
            }
        }

        Debug.Log("パズルの読み込み完了！");
        return true;
    }

    // 毎フレームの処理
    private void Update()
    {
        if (!boardReady || isClear || isRotating || mainCamera == null) return;
        if (!GetMouseClick(out Vector2 mousePosition)) return;

        Ray ray = mainCamera.ScreenPointToRay(mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            Transform clickedObject = hit.collider.transform;

            while (clickedObject != null && clickedObject.parent != transform)
                clickedObject = clickedObject.parent;

            if (clickedObject == null || !clickedObject.name.StartsWith("Tile_R")) return;
            StartCoroutine(RotateTile(clickedObject));
        }
    }

    // マウスクリック取得
    private bool GetMouseClick(out Vector2 mousePosition)
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            mousePosition = Mouse.current.position.ReadValue();
            return true;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
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

    // タイル回転処理
    private IEnumerator RotateTile(Transform tile)
    {
        isRotating = true;

        Quaternion startRotation = tile.localRotation;
        Quaternion targetRotation = Quaternion.Euler(tile.localEulerAngles + new Vector3(0f, 90f, 0f));
        float elapsedTime = 0f;
        float duration = Mathf.Max(0.01f, rotationDuration);

        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsedTime / duration));
            tile.localRotation = Quaternion.Slerp(startRotation, targetRotation, t);
            yield return null;
        }

        tile.localRotation = targetRotation;
        rotationCount++;

        Debug.Log($"{tile.name} 回転！ 合計：{rotationCount}回");

        // 回転するたびに植物を成長させる
        GrowPlant();

        // 回転後にパズルの接続を確認
        CheckPuzzleClear();
        isRotating = false;
    }

    // ========================================
    // 植物の成長処理
    // ========================================

    private void GrowPlant()
    {
        // 成長度を加算（最大100%）
        plantGrowth = Mathf.Clamp(plantGrowth + growthPerRotation, 0, 100);

        Debug.Log($"植物の成長度：{plantGrowth}%");

        // 成長度に応じて見た目を更新
        UpdatePlantVisual();
    }

    // 成長段階に応じて植物の表示を切り替える
    private void UpdatePlantVisual()
    {
        if (plantStages == null || plantStages.Length == 0) return;

        int stage = 0;

        if (plantGrowth >= 90) stage = 3;
        else if (plantGrowth >= 60) stage = 2;
        else if (plantGrowth >= 30) stage = 1;

        // 対応する段階の植物だけ表示する
        for (int i = 0; i < plantStages.Length; i++)
        {
            if (plantStages[i] != null) plantStages[i].SetActive(i == stage);
        }

        Debug.Log($"植物の成長段階：Stage{stage}");
    }

    // ========================================
    // パイプの接続方向
    // ========================================

    private Direction GetOpenDirections(int row, int col)
    {
        int rotation = Mathf.RoundToInt(tiles[row, col].localEulerAngles.y / 90f) % 4;

        if (pipeTypes[row, col] == PipeType.Straight)
            return rotation % 2 == 0 ? Direction.East | Direction.West : Direction.North | Direction.South;

        switch (rotation)
        {
            case 0: return Direction.North | Direction.East;
            case 1: return Direction.East | Direction.South;
            case 2: return Direction.South | Direction.West;
            case 3: return Direction.West | Direction.North;
        }

        return Direction.None;
    }

    private bool HasOpening(Direction openings, Direction direction)
    {
        return (openings & direction) != Direction.None;
    }

    private Direction GetOpposite(Direction direction)
    {
        switch (direction)
        {
            case Direction.North: return Direction.South;
            case Direction.East: return Direction.West;
            case Direction.South: return Direction.North;
            case Direction.West: return Direction.East;
        }

        return Direction.None;
    }

    // ========================================
    // パズルの接続判定
    // ========================================

    private void CheckPuzzleClear()
    {
        if (!boardReady || isClear) return;

        Direction[,] openings = new Direction[GridSize, GridSize];

        for (int row = 0; row < GridSize; row++)
            for (int col = 0; col < GridSize; col++)
                openings[row, col] = GetOpenDirections(row, col);

        // スタートの入口を確認
        if (!HasOpening(openings[0, 0], Direction.West)) return;

        bool[,] visited = new bool[GridSize, GridSize];
        Queue<Vector2Int> queue = new Queue<Vector2Int>();

        queue.Enqueue(new Vector2Int(0, 0));
        visited[0, 0] = true;

        Direction[] directions = { Direction.North, Direction.East, Direction.South, Direction.West };
        int[] rowOffsets = { -1, 0, 1, 0 };
        int[] colOffsets = { 0, 1, 0, -1 };

        // 幅優先探索
        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            int row = current.y;
            int col = current.x;

            // ゴールに到達したか
            if (row == GridSize - 1 && col == GridSize - 1 && HasOpening(openings[row, col], Direction.South))
            {
                PuzzleClear();
                return;
            }

            for (int i = 0; i < directions.Length; i++)
            {
                int nextRow = row + rowOffsets[i];
                int nextCol = col + colOffsets[i];

                if (nextRow < 0 || nextRow >= GridSize || nextCol < 0 || nextCol >= GridSize) continue;
                if (visited[nextRow, nextCol]) continue;
                if (!HasOpening(openings[row, col], directions[i])) continue;
                if (!HasOpening(openings[nextRow, nextCol], GetOpposite(directions[i]))) continue;

                visited[nextRow, nextCol] = true;
                queue.Enqueue(new Vector2Int(nextCol, nextRow));
            }
        }
    }

    // クリア処理
    private void PuzzleClear()
    {
        isClear = true;
        Debug.Log($"CLEAR！クリアまでの回転回数：{rotationCount}回");
    }

    // ========================================
    // 画面表示
    // ========================================

    // 画面表示
    private void OnGUI()
    {
        // 成長度のタイトル
        GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 18;
        labelStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(20, 15, 280, 30), "植物の成長度", labelStyle);

        // 元のGUIカラーを保存
        Color originalColor = GUI.color;

        // バーの外枠
        GUI.color = new Color(0.6f, 0.7f, 0.7f, 1f);
        GUI.DrawTexture(new Rect(20, 48, 264, 28), Texture2D.whiteTexture);

        // バーの背景
        GUI.color = new Color(0.08f, 0.1f, 0.1f, 1f);
        GUI.DrawTexture(new Rect(22, 50, 260, 24), Texture2D.whiteTexture);

        // 成長度に応じて緑色のバーを伸ばす
        float progress = Mathf.Clamp01(plantGrowth / 100f);
        GUI.color = new Color(0.2f, 1f, 0.55f, 1f);
        GUI.DrawTexture(new Rect(22, 50, 260f * progress, 24), Texture2D.whiteTexture);

        // GUIカラーを元に戻す
        GUI.color = originalColor;

        // クリア表示
        if (!isClear) return;

        GUIStyle clearStyle = new GUIStyle(GUI.skin.label);
        clearStyle.fontSize = 48;
        clearStyle.fontStyle = FontStyle.Bold;
        clearStyle.alignment = TextAnchor.MiddleCenter;
        clearStyle.normal.textColor = Color.green;
        GUI.Label(new Rect(0, Screen.height / 2f - 40f, Screen.width, 80f), "CLEAR!", clearStyle);
    }
}