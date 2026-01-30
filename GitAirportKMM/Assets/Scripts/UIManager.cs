using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    // === Singleton ===
    public static UIManager Instance { get; private set; }

    [Header("Opening Settings")]
    public Image openingImage;

    [Header("Tutorial UI")]
    public GameObject tutorialBanner;
    public float openingDisplayTime = 3f;
    public float fadeOutDuration = 1.5f;



    [Header("Ingame UI")]
    public Text nLevel;
    public Text PlayerHP;
    public Text ItemCounttxt;
    public Text fDifficulty_lv;

    [Header("HP Icon UI")]
    public Transform hpContainer;    // parent transform for hearts (e.g. a HorizontalLayoutGroup)
    public GameObject hpIconPrefab;  // prefab with an Image component using HPicon.png

    private readonly List<GameObject> hpIcons = new List<GameObject>();
    private int lastHp = -1;


    public Button StartButton;
    public Button clearButton;
    public Button gameoverButton;
    public GameObject gameOverPanel;

    public Image clearImage;
    public Image StartImage;

    [Header("Day / Goal UI")]
    public Text dayText;         // e.g. "Day 1"
    public Text missionText;     // "Collect 10 trash items"
    public Text timerText;       // "00:39"
    public Text goalProgressText;// "3 / 10"

    [Header("Break Time UI")]
    public GameObject breakTimeRoot; // same as GameManager.breakTimePanel, or parent of that
    public Button nextDayButton;

  

    [Header("Gauge UI")]
    public GameObject gaugeBlockPrefab;
    public Transform gaugeContainer;

    [Header("Warnings")]
    public GameObject warningTextObj;
    private Coroutine warningRoutine;

    [Header("Damage Effect")]
    // UICanvas_ingame 안의 REDMASK 오브젝트를 여기다 할당
    public GameObject redMask;

    private Coroutine damageRoutine;
    private readonly WaitForSeconds damageWait = new WaitForSeconds(0.15f);

    [Header("Refs")]
    public Player player;

    private void Awake()
    {
        // Singleton 세팅
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        // Player 자동 찾기
        if (player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
                player = playerObj.GetComponent<Player>();
        }

    
        if (clearImage != null)
        {
            clearImage.transform.position = new Vector3(0, 0);
            clearImage.gameObject.SetActive(false);
        }

        // REDMASK 처음에는 꺼두기
        if (redMask != null)
        {   
            
            redMask.transform.position += Vector3.right * -1200;
            redMask.SetActive(false);
}
         
        if (warningTextObj != null)
            warningTextObj.SetActive(false);

        if (nextDayButton != null)
        {
            nextDayButton.onClick.RemoveAllListeners();
            nextDayButton.onClick.AddListener(() =>
            {
                GameManager.Instance?.GoToNextDay();
            });
        }

        if (openingImage != null)
        {
            StartCoroutine(OpeningSequenceCoroutine());
        }
    }

    private void Update()
    {
        HandleInput();

        if (player != null)
            UpdateHPUI(player.currentHP);
            
         if (gaugeBlockPrefab != null || gaugeContainer != null)   
        UpdateItemcntUI(GameManager.Instance.itemCount);
        
    }

    private void HandleInput()
    {
        if (Time.timeScale == 0f) return;
        if (player == null || GameManager.Instance == null)
            return;

        if (Input.GetMouseButtonDown(0))
        {
            Vector3 clickWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            clickWorldPos.z = 0f;

            Vector2Int currentHex = player.WorldToHex(player.transform.position);

            // 클릭한 곳에서 플레이어 위치로의 방향벡터
            Vector3 diff = clickWorldPos - player.transform.position;

            // 6방향 정의 (Flat-Top Hex 기준)
            Vector2Int[] hexDirections = {
                new Vector2Int(1, 0),    // E
                new Vector2Int(0, 1),    // NE
                new Vector2Int(-1, 1),   // NW
                new Vector2Int(-1, 0),   // W
                new Vector2Int(0, -1),   // SW
                new Vector2Int(1, -1)    // SE
            };

            // 각 방향별 월드 좌표 벡터 계산
            float width = 1f;
            float height = Mathf.Sqrt(3f) / 2f * width;
            Vector3[] dirVectors = new Vector3[6];
            for (int i = 0; i < 6; i++)
            {
                dirVectors[i] = player.HexToWorld(currentHex + hexDirections[i]) - player.HexToWorld(currentHex);
            }

            // 클릭한 벡터와 각 방향의 내적값(유사도) 최대인 방향 고르기
            int bestDirIndex = 0;
            float bestDot = -Mathf.Infinity;
            for (int i = 0; i < 6; i++)
            {
                float dot = Vector3.Dot(diff.normalized, dirVectors[i].normalized);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    bestDirIndex = i;
                }
            }
            Vector2Int direction = hexDirections[bestDirIndex];

            // 최대 이동 거리: Shift 누르면 3칸, 기본 1칸
            int maxStep = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 3 : 1;

            // 경로 생성(한 칸씩, 막히면 중단)
            List<Vector2Int> path = new List<Vector2Int>();
            Vector2Int next = currentHex;
            for (int step = 1; step <= maxStep; step++)
            {
                Vector2Int candidate = currentHex + direction * step;
                if (GameManager.Instance.IsCellExists(candidate))
                {
                    // Check for item blocking
                    bool blocked = false;
                    Vector3 worldPos = player.HexToWorld(candidate);

                    // Since Item is a MonoBehaviour with a collider (implied by OnTriggerEnter2D),
                    // we can check if there's an Item at this world position.
                    // We can use OverlapPoint or similar, but since we are looking for a specific component...
                    // Let's use OverlapCircle for a small radius to catch the collider.
                    Collider2D[] hitColliders = Physics2D.OverlapCircleAll(worldPos, 0.1f);
                    foreach (var hit in hitColliders)
                    {
                        Item item = hit.GetComponent<Item>();
                        if (item != null)
                        {
                            if (!item.IsUnlockable())
                            {
                                blocked = true;
                                SoundManager.Instance?.Play(SoundManager.SoundId.Tooltip);
                                ShowItemLockedWarning();

                                break;
                            }
                        }
                    }

                    if (blocked)
                    {
                        break; // Blocked by locked item
                    }

                    path.Add(candidate);
                }
                else
                    break; // 못가는 셀이면 여기서 멈춤
            }
            if (path.Count > 0)
                player.MoveByPath(path);
        }
    }


 public void UpdateItemcntUI(int itemCount)
    {
        if (ItemCounttxt != null)
            ItemCounttxt.text = "Item: " + itemCount.ToString();
    }

    public void UpdateHPUI(int hp)
    {
         if (PlayerHP != null)
                PlayerHP.text = "HP: " + hp.ToString();
     

        hp = Mathf.Max(0, hp);
        if (hp == lastHp)
            return;

        lastHp = hp;

        // ensure we have enough icons
        while (hpIcons.Count < hp)
        {
            GameObject icon = Instantiate(hpIconPrefab, hpContainer);
            hpIcons.Add(icon);
        }

        // toggle icons on/off
        for (int i = 0; i < hpIcons.Count; i++)
        {
            bool shouldShow = (i < hp);
            if (hpIcons[i] != null && hpIcons[i].activeSelf != shouldShow)
                hpIcons[i].SetActive(shouldShow);
        }

//        if (PlayerHP != null)           PlayerHP.gameObject.SetActive(false); // hide old numeric text
    }

    public void UpdateItemGauge(int count)
    {
        if (gaugeBlockPrefab == null || gaugeContainer == null) return;

        count = Mathf.Max(0, count);
        int currentBlocks = gaugeContainer.childCount;

        if (count > currentBlocks)
        {
            int blocksToAdd = count - currentBlocks;
            for (int i = 0; i < blocksToAdd; i++)
                Instantiate(gaugeBlockPrefab, gaugeContainer);
        }
        else if (count < currentBlocks)
        {
            for (int i = currentBlocks - 1; i >= count; i--)
                Destroy(gaugeContainer.GetChild(i).gameObject);
        }

    }

    public void UpdateDayLabel(int dayNumber, string goalDescription)
    {
        if (dayText != null)
            dayText.text = $"Day {dayNumber}";

        if (missionText != null)
            missionText.text = goalDescription;
    }

    public void UpdateTimer(float remainingSeconds)
    {
        if (timerText == null) return;

        remainingSeconds = Mathf.Max(0f, remainingSeconds);
        int minutes = Mathf.FloorToInt(remainingSeconds / 60f);
        int seconds = Mathf.FloorToInt(remainingSeconds % 60f);

        timerText.text = $"{minutes:00}:{seconds:00}";
    }

    public void UpdateGoalProgress(int current, int target)
    {
        if (goalProgressText != null)
            goalProgressText.text = $"{current} / {target}";
    }

    public void ShowGameOverUI(bool show)
    {
        if (gameOverPanel != null)
            gameOverPanel.SetActive(show);

        if (gameoverButton != null)
            gameoverButton.gameObject.SetActive(show);

        if (show && gameoverButton != null)
        {
            gameoverButton.onClick.RemoveAllListeners();
            gameoverButton.onClick.AddListener(() =>
            {
                GameManager.Instance?.RestartFromDayOne();
            });
        }
    }

    public void GameStart()
    {
        Time.timeScale = 1f;
        if (StartImage != null)
            StartImage.gameObject.SetActive(false);
    }

    public void GameClear()
    {
        Time.timeScale = 0f;
        if (clearImage != null)
            clearImage.gameObject.SetActive(true);
    }

    public void ShowBreakTimeUI(bool show)
    {
        if (breakTimeRoot != null)
            breakTimeRoot.SetActive(show);
    }

    public void ShowItemLockedWarning()
    {
        if (warningTextObj == null)
            return;

        if (!gameObject.activeInHierarchy)
            return;

        if (warningRoutine != null)
            StopCoroutine(warningRoutine);

        warningRoutine = StartCoroutine(WarningTextCoroutine());
    }

    private IEnumerator WarningTextCoroutine()
    {
        warningTextObj.SetActive(true);
        yield return new WaitForSeconds(1.0f);
        warningTextObj.SetActive(false);
        warningRoutine = null;
    }

    public bool ShowTutorial(bool show)
    {
        if (show)
        {
            if (tutorialBanner != null)
            {
                tutorialBanner.SetActive(true);
                return true;
            }
            return false;
        }
        else
        {
            if (tutorialBanner != null)
                tutorialBanner.SetActive(false);
            return false;
        }
    }

    public void CloseTutorial()
    {
        ShowTutorial(false);
        GameManager.Instance?.OnTutorialClosed();
    }

    // === 피격시 빨간 화면 깜빡임 ===
    public void ShowDamageEffect()
    {
        if (redMask == null)
            return;

        if (!gameObject.activeInHierarchy)
            return;

        // 여러 번 맞을 때도 항상 새로 코루틴 돌도록
        if (damageRoutine != null)
            StopCoroutine(damageRoutine);

        damageRoutine = StartCoroutine(DamageFlashCoroutine());
    }

    private IEnumerator DamageFlashCoroutine()
    {
        redMask.SetActive(true);
        yield return damageWait;
        redMask.SetActive(false);
        damageRoutine = null;
    }

    // === 오프닝 페이드인/아웃 ===
    private IEnumerator OpeningSequenceCoroutine()
    {
        openingImage.gameObject.SetActive(true);
        openingImage.color = new Color(1, 1, 1, 1);   // 완전 불투명
        Time.timeScale = 0f;                          // 게임 일시정지

        float timer = 0f;
        bool isClicked = false;

        // 지정 시간 동안 또는 클릭까지 대기
        while (timer < openingDisplayTime && !isClicked)
        {
            if (Input.GetMouseButtonDown(0))
            {
                isClicked = true;
            }
            timer += Time.unscaledDeltaTime;
            yield return null;
        }

        // 페이드아웃
        float fadeTimer = 0f;
        while (fadeTimer < fadeOutDuration)
        {
            fadeTimer += Time.unscaledDeltaTime;
            float alpha = 1f - (fadeTimer / fadeOutDuration);
            openingImage.color = new Color(1, 1, 1, alpha);
            yield return null;
        }

        openingImage.gameObject.SetActive(false);
        Time.timeScale = 1f;  // 게임 재개
    }
}
