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

    [Header("通水演出設定")]
    [Tooltip("クリア判定が出たときに水を流すか")]
    [SerializeField] private bool playFlowOnClear = true;

    [Tooltip("水が進む速さ（1秒あたりに進む距離。1タイル=1の想定）")]
    [SerializeField, Min(0.05f)] private float waterFlowSpeed = 4f;

    [Tooltip("ゴール到達後に水を残す時間")]
    [SerializeField, Min(0f)] private float waterFlowHold = 0.15f;

    [Tooltip("水流ラインの太さ")]
    [SerializeField, Min(0f)] private float waterLineWidth = 0.12f;

    [Tooltip("水の先頭（水滴）の大きさ")]
    [SerializeField, Min(0f)] private float waterHeadSize = 0.18f;
    [SerializeField] private Color waterColor = new Color(0.25f, 0.8f, 1f, 1f);
    [SerializeField] private Color waterEmissionColor = new Color(0.2f, 0.7f, 1f, 1f);
    [SerializeField, Min(0f)] private float waterEmissionIntensity = 2f;

    [Tooltip("水が満ちたタイルを水色に寄せる強さ（0で色は変えない）")]
    [SerializeField, Range(0f, 1f)] private float waterTint = 0.6f;

    private bool isRotating = false;
    private bool isClear = false;
    private bool boardReady = false;
    private bool isFlowing = false;
    private int rotationCount = 0;
    private int plantGrowth = 0;
    private const int GridSize = 3;

    private enum PipeType { Straight, Corner }

    [System.Flags]
    private enum Direction { None = 0, North = 1, East = 2, South = 4, West = 8 }

    private readonly Transform[,] tiles = new Transform[GridSize, GridSize];
    private readonly PipeType[,] pipeTypes = new PipeType[GridSize, GridSize];

    // ---- 通水演出用のキャッシュ ----
    private List<Renderer>[,] tileRenderers;
    private List<Color>[,] tileBaseColors;
    private MaterialPropertyBlock propertyBlock;
    private GameObject waterHead;
    private LineRenderer waterLine;

    private static readonly int FillId = Shader.PropertyToID("_Fill");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    // ゲーム開始時
    private void Start()
    {
        if (mainCamera == null) mainCamera = Camera.main;

        // 植物を初期状態にする
        UpdatePlantVisual();

        boardReady = LoadTiles();
        if (boardReady)
        {
            CacheTileRenderers();
            CheckPuzzleClear();
        }
    }

    // タイルを読み込む
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
                    Debug.LogError($"{tileName} のパイプが不明です！");
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
        if (!boardReady || isClear || isRotating || isFlowing || mainCamera == null) return;
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

    // タイルを回転させる
    private IEnumerator RotateTile(Transform tile)
    {
        isRotating = true;

        // 回し始めたら水は引く
        ClearFlowVisual();

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

        Debug.Log($"{tile.name} 回転終了 累計：{rotationCount}回");

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
    // パイプの接続判定
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
        if (!boardReady || isClear || isFlowing) return;

        // ここで経路（通水ルート）を受け取る
        if (!TryGetFlowPath(out List<Vector2Int> path))
        {
            // 繋がっていないので水を引く
            ClearFlowVisual();
            return;
        }

        if (playFlowOnClear)
        {
            // 経路に沿って水を流してからクリア
            StartCoroutine(PlayWaterFlow(path));
        }
        else
        {
            PuzzleClear();
        }
    }

    /// <summary>
    /// スタート(0,0)の西口からゴール(2,2)の南口までの経路を幅優先探索で求める。
    /// 到達できれば true を返し、path にタイルの並び（x=列, y=行）を順番に入れる。
    /// 到達できなければ false。
    /// </summary>
    private bool TryGetFlowPath(out List<Vector2Int> path)
    {
        path = null;
        if (!boardReady) return false;

        Direction[,] openings = new Direction[GridSize, GridSize];

        for (int row = 0; row < GridSize; row++)
            for (int col = 0; col < GridSize; col++)
                openings[row, col] = GetOpenDirections(row, col);

        // スタートの開口を確認
        if (!HasOpening(openings[0, 0], Direction.West)) return false;

        bool[,] visited = new bool[GridSize, GridSize];
        Vector2Int[,] parents = new Vector2Int[GridSize, GridSize];

        for (int row = 0; row < GridSize; row++)
            for (int col = 0; col < GridSize; col++)
                parents[row, col] = new Vector2Int(-1, -1);

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

            // ゴールに到達した
            if (row == GridSize - 1 && col == GridSize - 1 && HasOpening(openings[row, col], Direction.South))
            {
                path = BuildPathFromParents(parents, current);
                return true;
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
                parents[nextRow, nextCol] = new Vector2Int(col, row);
                queue.Enqueue(new Vector2Int(nextCol, nextRow));
            }
        }

        return false;
    }

    // 親配列からゴール→スタートをたどり、スタート→ゴールの順に並べ直す
    private List<Vector2Int> BuildPathFromParents(Vector2Int[,] parents, Vector2Int goal)
    {
        List<Vector2Int> path = new List<Vector2Int>();
        Vector2Int cursor = goal;
        path.Add(cursor);

        // 念のため無限ループ防止
        int guard = GridSize * GridSize + 1;
        while ((cursor.x != 0 || cursor.y != 0) && guard-- > 0)
        {
            Vector2Int parent = parents[cursor.y, cursor.x];
            if (parent.x < 0 || parent.y < 0) break;
            path.Add(parent);
            cursor = parent;
        }

        path.Reverse();
        return path;
    }

    // ========================================
    // 通水演出
    // ========================================

    // 経路に沿って水を流す
    private IEnumerator PlayWaterFlow(List<Vector2Int> path)
    {
        if (path == null || path.Count < 2)
        {
            PuzzleClear();
            yield break;
        }

        isFlowing = true;

        // 水嵩をリセットして水流オブジェクトを用意
        ClearTileFill();
        EnsureFlowObjects();

        // 経路のローカル座標を作る（ラインは盤面の子なのでローカル空間で扱う）
        int count = path.Count;
        Vector3[] points = new Vector3[count];

        for (int i = 0; i < count; i++)
        {
            Transform tile = GetTile(path[i]);
            points[i] = tile != null ? transform.InverseTransformPoint(tile.position) : Vector3.zero;
        }

        // スタート地点に水滴を置く
        waterHead.SetActive(true);
        waterHead.transform.localPosition = points[0];

        waterLine.enabled = true;
        waterLine.positionCount = 1;
        waterLine.SetPosition(0, points[0]);

        // スタートタイルは最初から満水
        SetTileFill(path[0], 1f);

        float speed = Mathf.Max(0.05f, waterFlowSpeed);

        for (int i = 1; i < count; i++)
        {
            Vector3 from = points[i - 1];
            Vector3 to = points[i];

            // 距離に応じて時間を決める（どの区間でも同じ速さで流れる）
            float distance = Vector3.Distance(from, to);
            float duration = Mathf.Max(0.0001f, distance / speed);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                // 水滴を移動
                Vector3 head = Vector3.Lerp(from, to, t);
                waterHead.transform.localPosition = head;

                // 通過済みの区間＋先端までをラインで描く
                waterLine.positionCount = i + 1;
                waterLine.SetPosition(i, head);

                // 到着しつつあるタイルを満たしていく
                SetTileFill(path[i], t);

                yield return null;
            }

            // 区間の終点を確定
            SetTileFill(path[i], 1f);
            waterLine.SetPosition(i, to);
        }

        waterHead.SetActive(false);

        // 少しだけ水を残してからクリア表示
        if (waterFlowHold > 0f) yield return new WaitForSeconds(waterFlowHold);

        isFlowing = false;
        PuzzleClear();
    }

    // クリア処理
    private void PuzzleClear()
    {
        isClear = true;
        Debug.Log($"CLEAR！クリアまでの回転数：{rotationCount}回");
    }

    // 経路のタイル（x=列, y=行）を取得
    private Transform GetTile(Vector2Int cell)
    {
        if (cell.x < 0 || cell.x >= GridSize || cell.y < 0 || cell.y >= GridSize) return null;
        return tiles[cell.y, cell.x];
    }

    // 各タイルの Renderer と元の色を記憶しておく
    private void CacheTileRenderers()
    {
        tileRenderers = new List<Renderer>[GridSize, GridSize];
        tileBaseColors = new List<Color>[GridSize, GridSize];
        propertyBlock = new MaterialPropertyBlock();

        for (int row = 0; row < GridSize; row++)
        {
            for (int col = 0; col < GridSize; col++)
            {
                List<Renderer> renderers = new List<Renderer>();
                List<Color> colors = new List<Color>();

                Transform tile = tiles[row, col];

                if (tile != null)
                {
                    foreach (Renderer renderer in tile.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer == null) continue;
                        renderers.Add(renderer);
                        colors.Add(GetMaterialColor(renderer));
                    }
                }

                tileRenderers[row, col] = renderers;
                tileBaseColors[row, col] = colors;
            }
        }
    }

    // マテリアルの基本色を取得
    private static Color GetMaterialColor(Renderer renderer)
    {
        Material material = renderer.sharedMaterial;
        if (material == null) return Color.white;
        if (material.HasProperty(BaseColorId)) return material.GetColor(BaseColorId);
        if (material.HasProperty(ColorId)) return material.GetColor(ColorId);
        return Color.white;
    }

    // 水滴・水流ラインを必要になった時点で作る
    private void EnsureFlowObjects()
    {
        if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();

        // 水滴
        if (waterHead == null)
        {
            waterHead = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            waterHead.name = "WaterHead";
            waterHead.transform.SetParent(transform, false);
            waterHead.transform.localScale = Vector3.one * Mathf.Max(0.01f, waterHeadSize);

            // クリックのレイキャストに引っかからないようにコライダーを消す
            Collider collider = waterHead.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            Material headMaterial = CreateFlowMaterial(waterColor);
            Renderer headRenderer = waterHead.GetComponent<Renderer>();
            if (headRenderer != null && headMaterial != null) headRenderer.sharedMaterial = headMaterial;

            waterHead.SetActive(false);
        }

        // 水流ライン
        if (waterLine == null)
        {
            GameObject lineObject = new GameObject("WaterLine");
            lineObject.transform.SetParent(transform, false);

            waterLine = lineObject.AddComponent<LineRenderer>();
            waterLine.useWorldSpace = false;
            waterLine.widthMultiplier = Mathf.Max(0.001f, waterLineWidth);
            waterLine.numCapVertices = 4;
            waterLine.numCornerVertices = 4;
            waterLine.alignment = LineAlignment.View;
            waterLine.textureMode = LineTextureMode.Stretch;
            waterLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            waterLine.receiveShadows = false;
            waterLine.startColor = waterColor;
            waterLine.endColor = new Color(waterColor.r, waterColor.g, waterColor.b, 0f);
            waterLine.positionCount = 0;
            waterLine.enabled = false;

            Material lineMaterial = CreateFlowMaterial(waterColor);
            if (lineMaterial != null) waterLine.material = lineMaterial;
        }
    }

    // 実行時用の水マテリアルを作成
    private static Material CreateFlowMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) return null;

        Material material = new Material(shader);
        material.name = "RuntimeWaterFlow";

        if (material.HasProperty(BaseColorId)) material.SetColor(BaseColorId, color);
        if (material.HasProperty(ColorId)) material.SetColor(ColorId, color);

        return material;
    }

    // 指定タイルの水嵩を 0～1 で設定する
    private void SetTileFill(Vector2Int cell, float fill)
    {
        if (cell.x < 0 || cell.x >= GridSize || cell.y < 0 || cell.y >= GridSize) return;
        if (tileRenderers == null || propertyBlock == null) return;

        List<Renderer> renderers = tileRenderers[cell.y, cell.x];
        List<Color> baseColors = tileBaseColors[cell.y, cell.x];

        if (renderers == null || baseColors == null) return;

        float t = Mathf.Clamp01(fill);

        for (int i = 0; i < renderers.Count; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null) continue;

            Color tinted = Color.Lerp(baseColors[i], waterColor, t * waterTint);

            propertyBlock.Clear();

            // 自作シェーダーで水面を上げたい場合は _Fill を使用（無いマテリアルでは無視される）
            propertyBlock.SetFloat(FillId, t);
            propertyBlock.SetColor(BaseColorId, tinted);
            propertyBlock.SetColor(ColorId, tinted);
            propertyBlock.SetColor(EmissionColorId, waterEmissionColor * (waterEmissionIntensity * t));

            renderer.SetPropertyBlock(propertyBlock);
        }
    }

    // 全タイルの水嵩を 0 に戻す
    private void ClearTileFill()
    {
        for (int row = 0; row < GridSize; row++)
            for (int col = 0; col < GridSize; col++)
                SetTileFill(new Vector2Int(col, row), 0f);
    }

    // 水の表示をすべて消す
    private void ClearFlowVisual()
    {
        ClearTileFill();

        if (waterLine != null)
        {
            waterLine.positionCount = 0;
            waterLine.enabled = false;
        }

        if (waterHead != null) waterHead.SetActive(false);
    }

    // ========================================
    // 画面表示
    // ========================================

    private void OnGUI()
    {
        GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 18;
        labelStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(20, 15, 280, 30), "植物の成長度", labelStyle);

        Color originalColor = GUI.color;

        // バーの外枠
        GUI.color = new Color(0.6f, 0.7f, 0.7f, 1f);
        GUI.DrawTexture(new Rect(20, 48, 264, 28), Texture2D.whiteTexture);

        // バーの背景
        GUI.color = new Color(0.08f, 0.1f, 0.1f, 1f);
        GUI.DrawTexture(new Rect(22, 50, 260, 24), Texture2D.whiteTexture);

        // 成長度に応じてバーの色を変更
        Color barColor;
        if (plantGrowth >= 90)
            barColor = new Color(1f, 0.1f, 0.1f, 1f);
        else if (plantGrowth >= 60)
            barColor = new Color(1f, 0.45f, 0.05f, 1f);
        else if (plantGrowth >= 30)
            barColor = new Color(1f, 0.9f, 0.1f, 1f);
        else
            barColor = new Color(0.2f, 1f, 0.55f, 1f);

        // 成長度に応じてバーを伸ばす
        float progress = Mathf.Clamp01(plantGrowth / 100f);
        GUI.color = barColor;
        GUI.DrawTexture(new Rect(22, 50, Mathf.Lerp(0f, 260f, progress), 24), Texture2D.whiteTexture);

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
