using System;
using System.Collections;
using System.Collections.Generic;


using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum GameState
{
    DayActivity,
    BreakTime,
    GameOver
}

/// <summary>
/// 게임의 전반적인 진행, 상태, 규칙을 관리하는 싱글톤(Singleton) 클래스입니다.
/// 헥사곤 타일맵 생성, 아이템 관리, 게임 클리어 조건 판정 등 핵심 로직을 담당합니다.
/// </summary>
public class GameManager : MonoBehaviour
{
    public int TodayCount = 1;
    // --- 외부 참조 변수 --- //

    /// 게임 레벨이 시작된 후 흐른 시간을 반환합니다. (읽기 전용)
    public float GameTime => Time.timeSinceLevelLoad;

    /// <summary>
    /// 플레이어 오브젝트의 참조입니다. 인스펙터에서 직접 할당하거나, 없을 경우 Start()에서 자동으로 찾습니다.
    /// </summary>
    public Player player;

    /// <summary>
    /// 싱글톤 패턴을 위한 static 인스턴스입니다. 
    /// 다른 스크립트에서 'GameManager.Instance'를 통해 이 스크립트의 public 멤버에 쉽게 접근할 수 있습니다.
    /// </summary>
    public static GameManager Instance;

    // --- 그리드 설정 --- //
    [Header("Grid Settings")]
    public GameObject gridCellPrefab; // 헥사곤 타일(셀)로 사용될 프리팹입니다.
    public int gridWidth = 9;         // 그리드의 가로 셀 개수입니다.
    public int gridHeight = 9;        // 그리드의 세로 셀 개수입니다.
    public Transform gridParent;      // 생성된 셀들을 담을 부모 오브젝트의 Transform입니다.

    // --- 아이템 설정 --- //
    [Header("Item Settings")]
    public GameObject itemPrefab;     // '폐지' 등 플레이어가 수집할 아이템 프리팹입니다.

    // --- 게임 클리어 조건 설정 --- //
    [Header("Game Clear Settings")]
    public Text clearText; // "Day1 Stage Clear"와 같은 메시지를 표시할 UI Text 컴포넌트입니다.
    public int clearItemCount; // Day 클리어를 위해 필요한 아이템(폐지) 획득 개수입니다.
    public float clearTime = 600f; // Day 클리어를 위한 생존 시간 목표 (600초 = 10분) 입니다.

    [Header("Game Over Settings")]
    public Text gameOverText; // 게임 오버 시 표시할 UI 텍스트
    public Text WarningItemText; // 경고 UI 텍스트

    [Header("Day / Day Cycle Settings")]
    public DaySetting[] daySettings;   // set up in the Inspector
    public int fallbackMaxDay = 10;    // used if array is shorter than TodayCount

    public Text dayInfoText;           // optional: "Day 1" etc.
    public Text missionInfoText;       // optional: mission description
    public GameObject breakTimePanel;  // root panel shown during BreakTime

    private float dayElapsedTime = 0f; // current day’s elapsed time
    private DaySetting activeDaySetting;

    // --- 내부 상태 변수 --- //
    private Dictionary<Vector2Int, Cell> cellGrid = new Dictionary<Vector2Int, Cell>();
    private List<Cell> activeTrail = new List<Cell>();
    public int itemCount = 0;
    private bool isGameCleared = false;
    private bool isGameOver = false;
    private Coroutine stageEnemyRoutine;
    private Coroutine resumeSpawnRoutine;
    public GameState CurrentState { get; private set; } = GameState.DayActivity;

    [System.Serializable]
    public class DaySetting
    {
        public int dayNumber = 1;         // e.g. 1, 2, 3...
        public float timeLimit = 40f;     // seconds for this day
        public int targetItemCount = 10;  // items needed to clear
        [TextArea]
        public string goalDescription;    // text shown in UI (mission text)
    }

    /// <summary>
    /// 인스펙터에 daySettings가 비어있을 경우 기본 5개 일차 데이터를 생성합니다.
    /// </summary>
    private void InitializeStages()
    {
        if (daySettings != null && daySettings.Length > 0)
            return;

        int defaultDays = 5;
        int baseTarget = (clearItemCount > 0) ? clearItemCount : 10;

        daySettings = new DaySetting[defaultDays];
        for (int i = 0; i < defaultDays; i++)
        {
            int dayNumber = i + 1;
            int targetCount = baseTarget + (i * 5);

            daySettings[i] = new DaySetting
            {
                dayNumber = dayNumber,
                timeLimit = clearTime,
                targetItemCount = targetCount,
                goalDescription = $"Collect {targetCount} items"
            };
        }

        fallbackMaxDay = defaultDays;
    }

    /// <summary>
    /// 플레이어의 현재 월드 좌표를 반환합니다.
    /// </summary>
    public Vector3 GetPlayerPosition()
    {
        if (player != null)
            return player.transform.position;
        else
            return Vector3.zero;
    }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) { Destroy(gameObject); return; }

        if (gridParent == null) gridParent = this.transform;
    }

    private void Start()
    {
        GenerateGrid();
        InitializeStages();

        if (player == null)
            player = FindObjectOfType<Player>();

        if (clearText != null) clearText.gameObject.SetActive(false);
        if (gameOverText != null) gameOverText.gameObject.SetActive(false);
        if (WarningItemText != null) WarningItemText.gameObject.SetActive(false);
        if (breakTimePanel != null) breakTimePanel.SetActive(false);

        StartDay(TodayCount);
    }

    // ★★★ Update 로직 (수정됨) ★★★
    private void Update()
    {

        /*게임 오버 시:GameState.GameOver 상태인지 확인합니다.
화면을 터치(클릭)하면 RestartFromDayOne()을 호출하여 1일 차부터 재시작합니다.

클리어(쉬는 시간) 시: GameState.BreakTime 상태인지 확인합니다.
화면을 터치(클릭)하면 GoToNextDay()를 호출하여 **다음 스테이지(Day)**로 넘어갑니다.

게임 진행 중:
남은 시간을 계산하고 (activeDaySetting.timeLimit - dayElapsedTime) UI를 갱신합니다.
시간이 0이 되거나, 목표 아이템 개수를 채우면 EndDay()를 호출하여 하루를 마무리합니다.*/
        // 1. 게임 오버 상태일 때: 화면 탭 시 재시작
        if (CurrentState == GameState.GameOver)
        {
            if (Input.GetMouseButtonDown(0)) // 모바일 터치 및 마우스 왼쪽 클릭        
                RestartFromDayOne(); // 1일차부터 완전 재시작
            
            return;
        }

        // 2. 게임 클리어(쉬는 시간) 상태일 때: 화면 탭 시 다음 날로 이동
        if (CurrentState == GameState.BreakTime)
        {
            if (Input.GetMouseButtonDown(0))
                GoToNextDay();
            
            return;
        }

        // 3. 게임 진행 중 (DayActivity)
        if (CurrentState == GameState.DayActivity)
        {
            dayElapsedTime += Time.deltaTime;
            float remaining = Mathf.Max(0f, activeDaySetting.timeLimit - dayElapsedTime);
            UIManager.Instance?.UpdateTimer(remaining);

            // 시간 초과 체크
            if (remaining <= 0f)
            {
                EndDay(); // 기획에 따라 GameOver()로 변경 가능
                return;
            }

            // 목표 달성 체크
            if (itemCount >= activeDaySetting.targetItemCount)
            {
                EndDay();
                return;
            }
        }
    }

    public void PauseAllSpawners()
    {
        var spawners = FindObjectsOfType<EnemySpawner>(true);
        foreach (var sp in spawners) sp.PauseSpawning();

        var itemSpawners = FindObjectsOfType<ItemSpawner_Grid>(true);
        foreach (var sp in itemSpawners) sp.PauseSpawning();
    }

    public void ResumeAllSpawners()
    {
        var spawners = FindObjectsOfType<EnemySpawner>(true);
        foreach (var sp in spawners) sp.ResumeSpawningWithDelay(sp.spawnInterval);

        var itemSpawners = FindObjectsOfType<ItemSpawner_Grid>(true);
        foreach (var sp in itemSpawners) sp.ResumeSpawningWithDelay(sp.spawnInterval);
    }

    public void OnTutorialClosed()
    {
        ResumeAllSpawners();
    }

    public void StartDay(int dayNumber)
    {
        InitializeStages();
        // GameManager.cs - StartDay() 마지막 근처에 추가
        SoundManager.Instance?.PlayBGM(SoundManager.SoundId.Bgm_Day, 0.3f);

        TodayCount = Mathf.Max(1, dayNumber);

        if (daySettings != null && daySettings.Length > 0)
        {
            int index = Mathf.Clamp(TodayCount - 1, 0, daySettings.Length - 1);
            activeDaySetting = daySettings[index];
        }
        else
        {
            activeDaySetting = new DaySetting
            {
                dayNumber = TodayCount,
                timeLimit = clearTime,
                targetItemCount = clearItemCount,
                goalDescription = ""
            };
        }

        CurrentState = GameState.DayActivity;
        isGameCleared = false;
        isGameOver = false;
        Time.timeScale = 1f;

        itemCount = 0;
        dayElapsedTime = 0f;
        UIManager.Instance?.UpdateItemGauge(0);
        UIManager.Instance?.UpdateItemcntUI(0);
        UIManager.Instance?.UpdateGoalProgress(0, activeDaySetting.targetItemCount);

        if (clearText != null) clearText.gameObject.SetActive(false);
        if (gameOverText != null) gameOverText.gameObject.SetActive(false);
        if (WarningItemText != null) WarningItemText.gameObject.SetActive(false);
        if (breakTimePanel != null) breakTimePanel.SetActive(false);
        UIManager.Instance?.ShowGameOverUI(false);

        clearItemCount = activeDaySetting.targetItemCount;
        clearTime = activeDaySetting.timeLimit;

        if (dayInfoText != null)
            dayInfoText.text = $"Day {activeDaySetting.dayNumber}";
        if (missionInfoText != null)
            missionInfoText.text = activeDaySetting.goalDescription;

        UIManager.Instance?.UpdateDayLabel(activeDaySetting.dayNumber, activeDaySetting.goalDescription);
        UIManager.Instance?.UpdateGoalProgress(0, activeDaySetting.targetItemCount);
        UIManager.Instance?.UpdateTimer(activeDaySetting.timeLimit);

        ResetGridState();
        //        player?.GrantStageStartInvincibility(1f);
        OnStageStart();
        Debug.Log($"[Day] Start Day {TodayCount}, targetItems={clearItemCount}, timeLimit={clearTime}");
    }

   




    void GenerateGrid()
    {
        cellGrid = new Dictionary<Vector2Int, Cell>();
        activeTrail = new List<Cell>();

        float width = 1f;
        float height = Mathf.Sqrt(3f) / 2f * width;

        Vector2 offset = new Vector2(
            -width * 0.75f * (gridWidth - 1) / 2f,
            -height * (gridHeight - 1) / 2f
        );

        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                float xPos = width * 0.75f * x + offset.x;
                float yPos = height * (y + 0.5f * (x & 1)) + offset.y;

                Vector3 cellPos = new Vector3(xPos, yPos, 0);
                var obj = Instantiate(gridCellPrefab, cellPos, Quaternion.identity, gridParent);

                var cell = obj.GetComponent<Cell>();
                if (cell != null)
                {
                    Vector2Int hexCoord = new Vector2Int(x, y);
                    cell.hexCoords = hexCoord;
                    cellGrid[hexCoord] = cell;
                }
            }
        }
    }

    public void CollectItem()
    {
        if (isGameCleared) return;

        itemCount++;
        UIManager.Instance?.UpdateItemGauge(itemCount);
        UIManager.Instance?.UpdateGoalProgress(itemCount, activeDaySetting.targetItemCount);
        Debug.Log($"[Item] 획득: {itemCount}개");
        SoundManager.Instance?.Play(SoundManager.SoundId.ItemPickup);

        if (CurrentState == GameState.DayActivity &&
            itemCount >= activeDaySetting.targetItemCount)
            EndDay();
        
    }

    public void EndDay()
    {
        if (isGameCleared || isGameOver)
            return;

        isGameCleared = true;
        CurrentState = GameState.BreakTime;
        Time.timeScale = 0f;

        if (clearText != null)
        {
            clearText.text = $"Day {TodayCount} Clear";
            clearText.gameObject.SetActive(true);
        }

        if (breakTimePanel != null)
            breakTimePanel.SetActive(true);

        UIManager.Instance?.ShowBreakTimeUI(true);

        Debug.Log($"[Day] Day {TodayCount} cleared.");
        SoundManager.Instance?.StageClear();
    }

    public void GameOver()
    {
        if (isGameOver) return;

        isGameOver = true;
        CurrentState = GameState.GameOver;
        Time.timeScale = 0f;

        if (gameOverText != null)
            gameOverText.gameObject.SetActive(true);

        UIManager.Instance?.ShowBreakTimeUI(false);
        UIManager.Instance?.ShowGameOverUI(true);

        Debug.Log("Game Over");
        SoundManager.Instance?.GameOver();
    }

    public void RestartGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void GoToNextDay()
    {
        int maxConfigured = (daySettings != null && daySettings.Length > 0)
       ? daySettings.Length
       : fallbackMaxDay;

        TodayCount++;

        if (TodayCount > maxConfigured)
        {
            TodayCount = 1;
        }

        Debug.Log($"[Day] Go to next Day: {TodayCount}");

        Time.timeScale = 1f;

        // ✅ 클리어 후 다음날(혹은 리셋) 시작 시 HP/상태 초기화 + 하트 UI 갱신
        player?.ResetStatus(GetGridCenter());  // nHP=3 + UpdateHPUI 포함 :contentReference[oaicite:3]{index=3}

        StartDay(TodayCount);
    }

    public void RestartFromDayOne()
    {
        TodayCount = 1;
        Time.timeScale = 1f;
        UIManager.Instance?.UpdateItemGauge(0);
        UIManager.Instance?.UpdateItemcntUI(0);
        player?.ResetStatus(GetGridCenter());
        ResetGridState();
        StartDay(TodayCount);
    }

    private void ResetGridState()
    {
        activeTrail.Clear();

        foreach (var cell in cellGrid.Values)
        
            cell.SetState(CellState.Neutral);
        
    }

    public void OnStageStart()
    {
        // 1. Destroy existing enemies (cleanup)
        var enemies = GameObject.FindGameObjectsWithTag("Enemy");
        for (int i = 0; i < enemies.Length; i++)
            Destroy(enemies[i]);

        // 2. Tutorial Check
        bool tutorialShown = false;
        if (UIManager.Instance != null)
        {
            tutorialShown = UIManager.Instance.ShowTutorial(true);
        }

        if (tutorialShown)
        {
            PauseAllSpawners();
        }
        else
        {
            // If no tutorial, ensure game proceeds normally
            OnTutorialClosed();
        }
    }

    // --- 외부 제공 함수 및 프로퍼티들 --- //
    public int CurrentScore => itemCount;
    public List<Cell> CurrentTrail => activeTrail;

    public bool IsCellInTrail(Vector2Int hexPos)
    {
        Cell cell = GetCellAt(hexPos);
        return cell != null && cell.currentState == CellState.PlayerTrail;
    }

    public void ResetTrail()
    {
        var trailToReset = new List<Cell>(activeTrail);
        activeTrail.Clear();
        foreach (var cell in trailToReset)
        {
            cell.SetState(CellState.Neutral);
        }
    }

    #region Land Grabbing System

    public void RegisterTrailCell(Cell cell)
    {
        if (!activeTrail.Contains(cell))
        {
            activeTrail.Add(cell);
        }
    }

    public void DeregisterTrailCell(Cell cell)
    {
        activeTrail.Remove(cell);
    }

    public void ProcessLoop()
    {
        if (activeTrail.Count < 3) return;

        List<Vector2Int> loopCoords = new List<Vector2Int>();
        foreach (var cell in activeTrail)
        {
            loopCoords.Add(cell.hexCoords);
        }

        List<Vector2Int> enclosedArea = FindEnclosedArea(loopCoords);

        if (enclosedArea != null && enclosedArea.Count > 0)
        {
            Debug.Log($"[Territory] {enclosedArea.Count} cells captured!");
            foreach (var pos in enclosedArea)
            {
                GetCellAt(pos)?.SetState(CellState.PlayerCaptured);
            }
            GrantRewards(enclosedArea.Count);
        }

        List<Cell> trailToClear = new List<Cell>(activeTrail);
        foreach (var cell in trailToClear)
        {
            cell.SetState(CellState.Neutral);
        }
    }

    private List<Vector2Int> FindEnclosedArea(List<Vector2Int> loop)
    {
        HashSet<Vector2Int> loopSet = new HashSet<Vector2Int>(loop);
        List<Vector2Int> potentialFills = new List<Vector2Int>();

        foreach (var loopCellCoord in loop)
        {
            foreach (var neighbor in GetNeighbors(loopCellCoord))
            {
                if (!loopSet.Contains(neighbor) && IsCellExists(neighbor) && GetCellAt(neighbor).currentState == CellState.Neutral)
                {
                    potentialFills.Add(neighbor);
                }
            }
        }

        HashSet<Vector2Int> checkedStarts = new HashSet<Vector2Int>();

        foreach (var startNode in potentialFills)
        {
            if (checkedStarts.Contains(startNode)) continue;

            Queue<Vector2Int> queue = new Queue<Vector2Int>();
            queue.Enqueue(startNode);
            HashSet<Vector2Int> visited = new HashSet<Vector2Int> { startNode };
            checkedStarts.Add(startNode);
            bool touchesBoundary = false;

            while (queue.Count > 0)
            {
                Vector2Int current = queue.Dequeue();
                foreach (var neighbor in GetNeighbors(current))
                {
                    if (!IsCellExists(neighbor)) { touchesBoundary = true; break; }
                    if (visited.Contains(neighbor) || loopSet.Contains(neighbor)) continue;
                    visited.Add(neighbor);
                    checkedStarts.Add(neighbor);
                    queue.Enqueue(neighbor);
                }
                if (touchesBoundary) break;
            }

            if (!touchesBoundary)
            {
                return new List<Vector2Int>(visited);
            }
        }
        return null;
    }

    public Cell GetCellAt(Vector2Int pos)
    {
        cellGrid.TryGetValue(pos, out Cell cell);
        return cell;
    }

    public Vector2Int[] GetNeighbors(Vector2Int hex)
    {
        if ((hex.x & 1) == 0) // Even-q
        {
            return new[] {
                new Vector2Int(hex.x, hex.y + 1), new Vector2Int(hex.x + 1, hex.y), new Vector2Int(hex.x + 1, hex.y - 1),
                new Vector2Int(hex.x, hex.y - 1), new Vector2Int(hex.x - 1, hex.y - 1), new Vector2Int(hex.x - 1, hex.y)
            };
        }
        else // Odd-q
        {
            return new[] {
                new Vector2Int(hex.x, hex.y + 1), new Vector2Int(hex.x + 1, hex.y + 1), new Vector2Int(hex.x + 1, hex.y),
                new Vector2Int(hex.x, hex.y - 1), new Vector2Int(hex.x - 1, hex.y), new Vector2Int(hex.x - 1, hex.y + 1)
            };
        }
    }

    public void GrantRewards(int capturedCellCount)
    {
        itemCount += capturedCellCount;
        UIManager.Instance?.UpdateItemGauge(itemCount);
        Debug.Log($"[Territory] Gained {capturedCellCount} score! Total score: {itemCount}");
    }

    #endregion

    public Vector2Int GetGridDimensions() => new Vector2Int(gridWidth, gridHeight);
    public Vector2Int GetGridCenter() => new Vector2Int(gridWidth / 2, gridHeight / 2);

    public bool IsCellExists(Vector2Int pos)
    {
        return pos.x >= 0 && pos.x < gridWidth && pos.y >= 0 && pos.y < gridHeight;
    }
}
