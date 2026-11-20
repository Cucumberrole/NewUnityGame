using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class PipePuzzleController : MonoBehaviour
{
    [Header("カメラ設定")]
    [SerializeField] private Camera _mainCamera;

    [Header("回転設定")]
    [SerializeField] private float _rotationDuration = 0.25f;

    [Header("植物の成長設定")]
    [SerializeField, Min(1)] private int _growthPerRotation = 10;
    [SerializeField] private GameObject[] _plantStages = new GameObject[4];

    [Header("進捗バー点滅設定")]
    [Tooltip("成長度90%以上で点滅する速さ")]
    [SerializeField, Min(0f)] private float _blinkSpeed = 3f;

    [Tooltip("点滅時に最も暗くなる透明度")]
    [SerializeField, Range(0f, 1f)] private float _blinkMinAlpha = 0.3f;

    [Header("最大成長演出設定")]
    [Tooltip("カメラが揺れる時間")]
    [SerializeField, Min(0.1f)] private float _cameraShakeDuration = 1.2f;

    [Tooltip("カメラの揺れの強さ")]
    [SerializeField, Min(0f)] private float _cameraShakeStrength = 0.25f;

    [Tooltip("植物が巨大化する時間")]
    [SerializeField, Min(0.1f)] private float _plantScaleDuration = 3f;

    [Tooltip("植物の最終的な大きさ")]
    [SerializeField, Min(1f)] private float _plantScaleMultiplier = 2.5f;

    [Header("通水演出設定")]
    [Tooltip("クリア判定が出たときに水を流すか")]
    [SerializeField] private bool _playFlowOnClear = true;

    [Tooltip("水が進む速さ（1秒あたりに進む距離。1タイル=1の想定）")]
    [SerializeField, Min(0.05f)] private float _waterFlowSpeed = 4f;

    [Tooltip("ゴール到達後に水を残す時間")]
    [SerializeField, Min(0f)] private float _waterFlowHold = 0.15f;

    [Tooltip("水流ラインの太さ")]
    [SerializeField, Min(0f)] private float _waterLineWidth = 0.12f;

    [Tooltip("水の先頭（水滴）の大きさ")]
    [SerializeField, Min(0f)] private float _waterHeadSize = 0.18f;

    [SerializeField] private Color _waterColor = new Color(0.25f, 0.8f, 1f, 1f);
    [SerializeField] private Color _waterEmissionColor = new Color(0.2f, 0.7f, 1f, 1f);
    [SerializeField, Min(0f)] private float _waterEmissionIntensity = 2f;

    [Tooltip("水が満ちたタイルを水色に寄せる強さ（0で色は変えない）")]
    [SerializeField, Range(0f, 1f)] private float _waterTint = 0.6f;

    private bool _isRotating = false;
    private bool _isClear = false;
    private bool _boardReady = false;
    private bool _isFlowing = false;
    private bool _hasPlayedMaxGrowthEffect = false;

    private int _rotationCount = 0;
    private int _plantGrowth = 0;

    private float _blinkAlpha = 1f;
    private Vector3 _cameraInitialLocalPosition;
    private Vector3 _plantInitialLocalScale;

    private const int GridSize = 3;

    private enum PipeType
    {
        Straight,
        Corner
    }

    [System.Flags]
    private enum Direction
    {
        None = 0,
        North = 1,
        East = 2,
        South = 4,
        West = 8
    }

    private readonly Transform[,] _tiles = new Transform[GridSize, GridSize];
    private readonly PipeType[,] _pipeTypes = new PipeType[GridSize, GridSize];

    // 通水演出用のキャッシュ
    private List<Renderer>[,] _tileRenderers;
    private List<Color>[,] _tileBaseColors;
    private MaterialPropertyBlock _propertyBlock;

    private GameObject _waterHead;
    private LineRenderer _waterLine;

    private static readonly int FillId = Shader.PropertyToID("_Fill");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    // ゲーム開始時
    private void Start()
    {
        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
        }

        if (_mainCamera != null)
        {
            _cameraInitialLocalPosition = _mainCamera.transform.localPosition;
        }

        if (_plantStages != null && _plantStages.Length > 3 && _plantStages[3] != null)
        {
            _plantInitialLocalScale = _plantStages[3].transform.localScale;
        }

        UpdatePlantVisual();

        _boardReady = LoadTiles();

        if (_boardReady)
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

                _tiles[row, col] = tile;

                bool straight = false;
                bool horizontal = false;
                bool vertical = false;

                foreach (Transform child in tile.GetComponentsInChildren<Transform>(true))
                {
                    if (child == tile)
                    {
                        continue;
                    }

                    string childName = child.name.Trim();

                    if (childName.StartsWith("Pipe_Straight"))
                    {
                        straight = true;
                    }

                    if (childName.StartsWith("Pipe_Horizontal"))
                    {
                        horizontal = true;
                    }

                    if (childName.StartsWith("Pipe_Vertical") || childName.StartsWith("Pipe_Vartical"))
                    {
                        vertical = true;
                    }
                }

                if (horizontal && vertical && !straight)
                {
                    _pipeTypes[row, col] = PipeType.Corner;
                }
                else if (straight && !horizontal && !vertical)
                {
                    _pipeTypes[row, col] = PipeType.Straight;
                }
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
        UpdateBlinkEffect();

        if (!_boardReady || _isClear || _isRotating || _isFlowing || _mainCamera == null)
        {
            return;
        }

        if (!GetMouseClick(out Vector2 mousePosition))
        {
            return;
        }

        Ray ray = _mainCamera.ScreenPointToRay(mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            Transform clickedObject = hit.collider.transform;

            while (clickedObject != null && clickedObject.parent != transform)
            {
                clickedObject = clickedObject.parent;
            }

            if (clickedObject == null || !clickedObject.name.StartsWith("Tile_R"))
            {
                return;
            }

            StartCoroutine(RotateTile(clickedObject));
        }
    }

    // 進捗バーの点滅処理
    private void UpdateBlinkEffect()
    {
        if (_plantGrowth >= 90)
        {
            float blinkValue = (Mathf.Sin(Time.time * _blinkSpeed) + 1f) / 2f;
            _blinkAlpha = Mathf.Lerp(_blinkMinAlpha, 1f, blinkValue);
        }
        else
        {
            _blinkAlpha = 1f;
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
        _isRotating = true;

        ClearFlowVisual();

        Quaternion startRotation = tile.localRotation;
        Quaternion targetRotation = Quaternion.Euler(tile.localEulerAngles + new Vector3(0f, 90f, 0f));

        float elapsedTime = 0f;
        float duration = Mathf.Max(0.01f, _rotationDuration);

        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;

            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsedTime / duration));

            tile.localRotation = Quaternion.Slerp(startRotation, targetRotation, t);

            yield return null;
        }

        tile.localRotation = targetRotation;
        _rotationCount++;

        Debug.Log($"{tile.name} 回転終了 累計：{_rotationCount}回");

        GrowPlant();
        CheckPuzzleClear();

        _isRotating = false;
    }

    private void GrowPlant()
    {
        _plantGrowth = Mathf.Clamp(_plantGrowth + _growthPerRotation, 0, 100);

        Debug.Log($"植物の成長度：{_plantGrowth}%");

        UpdatePlantVisual();

        if (_plantGrowth >= 100 && !_hasPlayedMaxGrowthEffect)
        {
            _hasPlayedMaxGrowthEffect = true;
            PlayMaxGrowthEffect();
        }
    }

    private void PlayMaxGrowthEffect()
    {
        if (_mainCamera != null)
        {
            StartCoroutine(ShakeCamera());
        }

        if (_plantStages != null && _plantStages.Length > 3 && _plantStages[3] != null)
        {
            StartCoroutine(EnlargePlant());
        }
    }

    private IEnumerator ShakeCamera()
    {
        if (_mainCamera == null)
        {
            yield break;
        }

        float elapsedTime = 0f;
        float duration = Mathf.Max(0.1f, _cameraShakeDuration);

        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;

            float progress = Mathf.Clamp01(elapsedTime / duration);
            float strength = _cameraShakeStrength * (1f - progress);
            Vector2 shakeOffset = Random.insideUnitCircle * strength;

            _mainCamera.transform.localPosition = _cameraInitialLocalPosition
                + new Vector3(shakeOffset.x, shakeOffset.y, 0f);

            yield return null;
        }

        _mainCamera.transform.localPosition = _cameraInitialLocalPosition;
    }

    private IEnumerator EnlargePlant()
    {
        GameObject finalPlant = _plantStages[3];

        if (finalPlant == null)
        {
            yield break;
        }

        Vector3 startScale = _plantInitialLocalScale;
        Vector3 targetScale = _plantInitialLocalScale * _plantScaleMultiplier;

        float elapsedTime = 0f;
        float duration = Mathf.Max(0.1f, _plantScaleDuration);

        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;

            float progress = Mathf.Clamp01(elapsedTime / duration);
            float smoothProgress = Mathf.SmoothStep(0f, 1f, progress);

            finalPlant.transform.localScale = Vector3.Lerp(
                startScale,
                targetScale,
                smoothProgress
            );

            yield return null;
        }

        finalPlant.transform.localScale = targetScale;
    }

    // 成長段階に応じて植物の表示を切り替える
    private void UpdatePlantVisual()
    {
        if (_plantStages == null || _plantStages.Length == 0)
        {
            return;
        }

        int stage = 0;

        if (_plantGrowth >= 90)
        {
            stage = 3;
        }
        else if (_plantGrowth >= 60)
        {
            stage = 2;
        }
        else if (_plantGrowth >= 30)
        {
            stage = 1;
        }

        for (int i = 0; i < _plantStages.Length; i++)
        {
            if (_plantStages[i] != null)
            {
                _plantStages[i].SetActive(i == stage);
            }
        }

        Debug.Log($"植物の成長段階：Stage{stage}");
    }

    private Direction GetOpenDirections(int row, int col)
    {
        int rotation = Mathf.RoundToInt(_tiles[row, col].localEulerAngles.y / 90f) % 4;

        if (_pipeTypes[row, col] == PipeType.Straight)
        {
            return rotation % 2 == 0
                ? Direction.East | Direction.West
                : Direction.North | Direction.South;
        }

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

    private void CheckPuzzleClear()
    {
        if (!_boardReady || _isClear || _isFlowing)
        {
            return;
        }

        if (!TryGetFlowPath(out List<Vector2Int> path))
        {
            ClearFlowVisual();
            return;
        }

        if (_playFlowOnClear)
        {
            StartCoroutine(PlayWaterFlow(path));
        }
        else
        {
            PuzzleClear();
        }
    }

    /// <summary>
    /// スタート(0,0)の西口からゴール(2,2)の南口までの経路を幅優先探索で求める。
    /// </summary>
    private bool TryGetFlowPath(out List<Vector2Int> path)
    {
        path = null;

        if (!_boardReady)
        {
            return false;
        }

        Direction[,] openings = new Direction[GridSize, GridSize];

        for (int row = 0; row < GridSize; row++)
        {
            for (int col = 0; col < GridSize; col++)
            {
                openings[row, col] = GetOpenDirections(row, col);
            }
        }

        if (!HasOpening(openings[0, 0], Direction.West))
        {
            return false;
        }

        bool[,] visited = new bool[GridSize, GridSize];
        Vector2Int[,] parents = new Vector2Int[GridSize, GridSize];

        for (int row = 0; row < GridSize; row++)
        {
            for (int col = 0; col < GridSize; col++)
            {
                parents[row, col] = new Vector2Int(-1, -1);
            }
        }

        Queue<Vector2Int> queue = new Queue<Vector2Int>();

        queue.Enqueue(new Vector2Int(0, 0));
        visited[0, 0] = true;

        Direction[] directions =
        {
            Direction.North,
            Direction.East,
            Direction.South,
            Direction.West
        };

        int[] rowOffsets = { -1, 0, 1, 0 };
        int[] colOffsets = { 0, 1, 0, -1 };

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();

            int row = current.y;
            int col = current.x;

            if (row == GridSize - 1 &&
                col == GridSize - 1 &&
                HasOpening(openings[row, col], Direction.South))
            {
                path = BuildPathFromParents(parents, current);
                return true;
            }

            for (int i = 0; i < directions.Length; i++)
            {
                int nextRow = row + rowOffsets[i];
                int nextCol = col + colOffsets[i];

                if (nextRow < 0 || nextRow >= GridSize ||
                    nextCol < 0 || nextCol >= GridSize)
                {
                    continue;
                }

                if (visited[nextRow, nextCol])
                {
                    continue;
                }

                if (!HasOpening(openings[row, col], directions[i]))
                {
                    continue;
                }

                if (!HasOpening(openings[nextRow, nextCol], GetOpposite(directions[i])))
                {
                    continue;
                }

                visited[nextRow, nextCol] = true;
                parents[nextRow, nextCol] = new Vector2Int(col, row);

                queue.Enqueue(new Vector2Int(nextCol, nextRow));
            }
        }

        return false;
    }

    // 親配列から経路を作成する
    private List<Vector2Int> BuildPathFromParents(Vector2Int[,] parents, Vector2Int goal)
    {
        List<Vector2Int> path = new List<Vector2Int>();

        Vector2Int cursor = goal;
        path.Add(cursor);

        int guard = GridSize * GridSize + 1;

        while ((cursor.x != 0 || cursor.y != 0) && guard-- > 0)
        {
            Vector2Int parent = parents[cursor.y, cursor.x];

            if (parent.x < 0 || parent.y < 0)
            {
                break;
            }

            path.Add(parent);
            cursor = parent;
        }

        path.Reverse();

        return path;
    }

    private IEnumerator PlayWaterFlow(List<Vector2Int> path)
    {
        if (path == null || path.Count < 2)
        {
            PuzzleClear();
            yield break;
        }

        _isFlowing = true;

        ClearTileFill();
        EnsureFlowObjects();

        int count = path.Count;
        Vector3[] points = new Vector3[count];

        for (int i = 0; i < count; i++)
        {
            Transform tile = GetTile(path[i]);

            points[i] = tile != null
                ? transform.InverseTransformPoint(tile.position)
                : Vector3.zero;
        }

        _waterHead.SetActive(true);
        _waterHead.transform.localPosition = points[0];

        _waterLine.enabled = true;
        _waterLine.positionCount = 1;
        _waterLine.SetPosition(0, points[0]);

        SetTileFill(path[0], 1f);

        float speed = Mathf.Max(0.05f, _waterFlowSpeed);

        for (int i = 1; i < count; i++)
        {
            Vector3 from = points[i - 1];
            Vector3 to = points[i];

            float distance = Vector3.Distance(from, to);
            float duration = Mathf.Max(0.0001f, distance / speed);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;

                float t = Mathf.Clamp01(elapsed / duration);

                Vector3 head = Vector3.Lerp(from, to, t);

                _waterHead.transform.localPosition = head;

                _waterLine.positionCount = i + 1;
                _waterLine.SetPosition(i, head);

                SetTileFill(path[i], t);

                yield return null;
            }

            SetTileFill(path[i], 1f);
            _waterLine.SetPosition(i, to);
        }

        _waterHead.SetActive(false);

        if (_waterFlowHold > 0f)
        {
            yield return new WaitForSeconds(_waterFlowHold);
        }

        _isFlowing = false;

        PuzzleClear();
    }

    // クリア処理
    private void PuzzleClear()
    {
        _isClear = true;

        Debug.Log($"CLEAR！クリアまでの回転数：{_rotationCount}回");
    }

    // 経路のタイルを取得
    private Transform GetTile(Vector2Int cell)
    {
        if (cell.x < 0 || cell.x >= GridSize ||
            cell.y < 0 || cell.y >= GridSize)
        {
            return null;
        }

        return _tiles[cell.y, cell.x];
    }

    // 各タイルのRendererと元の色を記憶する
    private void CacheTileRenderers()
    {
        _tileRenderers = new List<Renderer>[GridSize, GridSize];
        _tileBaseColors = new List<Color>[GridSize, GridSize];

        _propertyBlock = new MaterialPropertyBlock();

        for (int row = 0; row < GridSize; row++)
        {
            for (int col = 0; col < GridSize; col++)
            {
                List<Renderer> renderers = new List<Renderer>();
                List<Color> colors = new List<Color>();

                Transform tile = _tiles[row, col];

                if (tile != null)
                {
                    foreach (Renderer renderer in tile.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer == null)
                        {
                            continue;
                        }

                        renderers.Add(renderer);
                        colors.Add(GetMaterialColor(renderer));
                    }
                }

                _tileRenderers[row, col] = renderers;
                _tileBaseColors[row, col] = colors;
            }
        }
    }

    // マテリアルの基本色を取得
    private static Color GetMaterialColor(Renderer renderer)
    {
        Material material = renderer.sharedMaterial;

        if (material == null)
        {
            return Color.white;
        }

        if (material.HasProperty(BaseColorId))
        {
            return material.GetColor(BaseColorId);
        }

        if (material.HasProperty(ColorId))
        {
            return material.GetColor(ColorId);
        }

        return Color.white;
    }

    // 水滴・水流ラインを作成する
    private void EnsureFlowObjects()
    {
        if (_propertyBlock == null)
        {
            _propertyBlock = new MaterialPropertyBlock();
        }

        if (_waterHead == null)
        {
            _waterHead = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _waterHead.name = "WaterHead";

            _waterHead.transform.SetParent(transform, false);
            _waterHead.transform.localScale = Vector3.one * Mathf.Max(0.01f, _waterHeadSize);

            Collider collider = _waterHead.GetComponent<Collider>();

            if (collider != null)
            {
                Destroy(collider);
            }

            Material headMaterial = CreateFlowMaterial(_waterColor);

            Renderer headRenderer = _waterHead.GetComponent<Renderer>();

            if (headRenderer != null && headMaterial != null)
            {
                headRenderer.sharedMaterial = headMaterial;
            }

            _waterHead.SetActive(false);
        }

        if (_waterLine == null)
        {
            GameObject lineObject = new GameObject("WaterLine");

            lineObject.transform.SetParent(transform, false);

            _waterLine = lineObject.AddComponent<LineRenderer>();
            _waterLine.useWorldSpace = false;
            _waterLine.widthMultiplier = Mathf.Max(0.001f, _waterLineWidth);
            _waterLine.numCapVertices = 4;
            _waterLine.numCornerVertices = 4;
            _waterLine.alignment = LineAlignment.View;
            _waterLine.textureMode = LineTextureMode.Stretch;
            _waterLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _waterLine.receiveShadows = false;
            _waterLine.startColor = _waterColor;
            _waterLine.endColor = new Color(_waterColor.r, _waterColor.g, _waterColor.b, 0f);
            _waterLine.positionCount = 0;
            _waterLine.enabled = false;

            Material lineMaterial = CreateFlowMaterial(_waterColor);

            if (lineMaterial != null)
            {
                _waterLine.material = lineMaterial;
            }
        }
    }

    // 実行時用の水マテリアルを作成
    private static Material CreateFlowMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader == null)
        {
            return null;
        }

        Material material = new Material(shader);
        material.name = "RuntimeWaterFlow";

        if (material.HasProperty(BaseColorId))
        {
            material.SetColor(BaseColorId, color);
        }

        if (material.HasProperty(ColorId))
        {
            material.SetColor(ColorId, color);
        }

        return material;
    }

    // 指定タイルの水嵩を設定する
    private void SetTileFill(Vector2Int cell, float fill)
    {
        if (cell.x < 0 || cell.x >= GridSize ||
            cell.y < 0 || cell.y >= GridSize)
        {
            return;
        }

        if (_tileRenderers == null || _propertyBlock == null)
        {
            return;
        }

        List<Renderer> renderers = _tileRenderers[cell.y, cell.x];
        List<Color> baseColors = _tileBaseColors[cell.y, cell.x];

        if (renderers == null || baseColors == null)
        {
            return;
        }

        float t = Mathf.Clamp01(fill);

        for (int i = 0; i < renderers.Count; i++)
        {
            Renderer renderer = renderers[i];

            if (renderer == null)
            {
                continue;
            }

            Color tinted = Color.Lerp(baseColors[i], _waterColor, t * _waterTint);

            _propertyBlock.Clear();

            _propertyBlock.SetFloat(FillId, t);
            _propertyBlock.SetColor(BaseColorId, tinted);
            _propertyBlock.SetColor(ColorId, tinted);
            _propertyBlock.SetColor(EmissionColorId, _waterEmissionColor * (_waterEmissionIntensity * t));

            renderer.SetPropertyBlock(_propertyBlock);
        }
    }

    // 全タイルの水嵩を0に戻す
    private void ClearTileFill()
    {
        for (int row = 0; row < GridSize; row++)
        {
            for (int col = 0; col < GridSize; col++)
            {
                SetTileFill(new Vector2Int(col, row), 0f);
            }
        }
    }

    // 水の表示をすべて消す
    private void ClearFlowVisual()
    {
        ClearTileFill();

        if (_waterLine != null)
        {
            _waterLine.positionCount = 0;
            _waterLine.enabled = false;
        }

        if (_waterHead != null)
        {
            _waterHead.SetActive(false);
        }
    }

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

        if (_plantGrowth >= 90)
        {
            barColor = new Color(1f, 0.1f, 0.1f, 1f);
        }
        else if (_plantGrowth >= 60)
        {
            barColor = new Color(1f, 0.45f, 0.05f, 1f);
        }
        else if (_plantGrowth >= 30)
        {
            barColor = new Color(1f, 0.9f, 0.1f, 1f);
        }
        else
        {
            barColor = new Color(0.2f, 1f, 0.55f, 1f);
        }

        // 成長度90%以上の場合は透明度を変化させる
        barColor.a = _blinkAlpha;

        // 成長度に応じてバーを伸ばす
        float progress = Mathf.Clamp01(_plantGrowth / 100f);

        GUI.color = barColor;

        GUI.DrawTexture(
            new Rect(22, 50, Mathf.Lerp(0f, 260f, progress), 24),
            Texture2D.whiteTexture
        );

        GUI.color = originalColor;

        // クリア表示
        if (!_isClear)
        {
            return;
        }

        GUIStyle clearStyle = new GUIStyle(GUI.skin.label);

        clearStyle.fontSize = 48;
        clearStyle.fontStyle = FontStyle.Bold;
        clearStyle.alignment = TextAnchor.MiddleCenter;
        clearStyle.normal.textColor = Color.green;

        GUI.Label(
            new Rect(0, Screen.height / 2f - 40f, Screen.width, 80f),
            "CLEAR!",
            clearStyle
        );
    }
}