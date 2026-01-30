using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.RuleTile.TilingRuleOutput;

public class Item : MonoBehaviour
{
    // ✅ 노란색으로 하이라이트된 셀들
    //    - 아이템이 놓인 셀(센터)
    //    - 인접 셀 1개 (랜덤)
    private List<Cell> highlightedCells = new List<Cell>();

    // ✅ 플레이어가 실제로 밟아서 "클리어"한 셀들
    //    - 중복 체크를 쉽게 하기 위해 HashSet 사용
    private HashSet<Cell> capturedCells = new HashSet<Cell>();

    // 아이템이 이미 수집되었는지 여부 (중복 수집 방지용)
    private bool isCollected = false;

    [Header("Lifetime")]
    [SerializeField] private float lifetime = 10f; // 인스펙터에서 조절 가능한 수명
    private float currentLifetime;

    [Header("Reward (optional)")]
    // 필요하면 Collect()에서 사용 가능한 수치들
    [SerializeField] private int deltaHp = 5;
    [SerializeField] private int deltaScore = 1;

    private void Awake()
    {
        // 최초 생성 시 수명 초기화
        currentLifetime = lifetime;
    }

    private void OnEnable()
    {
        // 풀링 대비: 매번 상태 초기화
        currentLifetime = lifetime;
        isCollected = false;
        highlightedCells.Clear();
        capturedCells.Clear();

        if (GameManager.Instance == null || GameManager.Instance.player == null)
            return;

        // 1. 아이템이 놓인 헥스 좌표 계산
        Vector2Int centerHex = GameManager.Instance.player.WorldToHex(transform.position);

        // 2. 센터 셀: 항상 노랗게 하이라이트
        Cell centerCell = null;
        if (GameManager.Instance.IsCellExists(centerHex))
        {
            centerCell = GameManager.Instance.GetCellAt(centerHex);
            if (centerCell != null)
            {
                centerCell.AddHighlight();          // 센터 셀 노란색
                highlightedCells.Add(centerCell);   // 퍼즐 대상에 포함
            }
        }

        // 3. 인접 셀들 중 "중립(Neutral)" 상태인 셀만 후보로 모으기
        List<Cell> neighborCandidates = new List<Cell>();

        foreach (var n in GameManager.Instance.GetNeighbors(centerHex))
        {
            if (!GameManager.Instance.IsCellExists(n))
                continue;

            Cell cell = GameManager.Instance.GetCellAt(n);
            if (cell != null && cell.currentState == CellState.Neutral)
            {
                neighborCandidates.Add(cell);
            }
        }

        // 4. 후보 중 1개만 랜덤으로 골라 노란색 하이라이트
        if (neighborCandidates.Count > 0)
        {
            int index = UnityEngine.Random.Range(0, neighborCandidates.Count);
            Cell selectedNeighbor = neighborCandidates[index];

            selectedNeighbor.AddHighlight();       // 인접 셀 노란색
            highlightedCells.Add(selectedNeighbor);
        }
        // else:
        //  인접에 Neutral 셀이 하나도 없으면 센터 셀만 퍼즐 대상으로 사용
        //  (highlightedCells 리스트에는 최소한 센터가 들어 있을 수 있음)

        // 만약 센터 셀도 없어서 highlightedCells.Count == 0 이라면,
        // 이 아이템은 그냥 시간 지나면 자연스럽게 사라지는 구조가 됨.
    }

    private void OnDisable()
    {
        // 아이템이 비활성화/파괴될 때 하이라이트 제거
        foreach (Cell cell in highlightedCells)
        {
            if (cell != null)
                cell.RemoveHighlight();
        }

        highlightedCells.Clear();
        capturedCells.Clear();
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        // ⚠️ 중요:
        // 아이템의 콜라이더에 플레이어가 닿았다고 바로 먹으면 안 된다.
        // 아이템 수집 조건은 "노란 셀들을 모두 밟았는가?" 이기 때문에
        // 여기에는 수집 로직을 넣지 않는다.
        //
        // 필요하다면 이펙트 / 사운드 재생 정도만 넣어도 된다.
    }

    private void Update()
    {
        // 1) 수명 처리
        currentLifetime -= Time.deltaTime;
        if (currentLifetime <= 0f)
        {
            // 제한 시간 끝 → 퍼즐 실패 느낌으로 그냥 사라짐
            Destroy(gameObject);
            return;
        }

        // 2) 플레이어가 어떤 하이라이트 셀을 실제로 밟았는지 갱신
        UpdateCapturedCells();

        // 3) 모든 노란 셀을 밟았는지 체크 (퍼즐 클리어 여부)
        CheckCellsCaptured();
    }

    /// <summary>
    /// 플레이어의 현재 헥스 좌표와 각 하이라이트 셀의 hexCoords를 비교해서,
    /// 실제로 밟은 셀을 capturedCells에 기록한다.
    /// </summary>
    private void UpdateCapturedCells()
    {
        if (GameManager.Instance == null || GameManager.Instance.player == null)
            return;

        // 플레이어가 현재 서 있는 헥스 좌표
        Vector2Int playerHex =
            GameManager.Instance.player.WorldToHex(GameManager.Instance.player.transform.position);

        foreach (Cell cell in highlightedCells)
        {
            if (cell == null)
                continue;

            // 아직 "밟았다"로 기록되지 않은 셀이고,
            // 그 셀의 hexCoords 와 플레이어의 hex 좌표가 같으면 → 방금 밟은 것
            if (cell.hexCoords == playerHex && !capturedCells.Contains(cell))
            {
                capturedCells.Add(cell);

                // 시각적 피드백: 그 셀에 트레일(파란색)을 깔고 싶다면 사용
                cell.ActivateTrail();
            }
        }
    }

    /// <summary>
    /// Checks if the item can be collected (i.e., if the player can step on the item's cell).
    /// This returns true only if all required neighbor cells have been captured.
    /// </summary>
    public bool IsUnlockable()
    {
        if (GameManager.Instance == null || GameManager.Instance.player == null)
            return true; // Fail-safe

        Vector2Int itemHex = GameManager.Instance.player.WorldToHex(transform.position);

        // Check all highlighted cells. If a cell is NOT the item's cell (center),
        // it must be in capturedCells for the item to be unlockable.
        foreach (var cell in highlightedCells)
        {
            if (cell == null) continue;

            // If this highlighted cell is a neighbor (not the center item cell)
            if (cell.hexCoords != itemHex)
            {
                // If the neighbor is not captured yet, the item is locked
                if (!capturedCells.Contains(cell))
                {
                    return false;
                }
            }
        }

        // All neighbors are captured
        return true;
    }

    /// <summary>
    /// Call Collect() only when the player has stepped on all highlighted cells
    /// (center + neighbor). We use capturedCells to track this.
    /// </summary>
    private void CheckCellsCaptured()
    {
        if (highlightedCells.Count == 0)
            return;

        // 아직 모든 하이라이트 셀을 밟지 않았다면 아무 것도 하지 않음
        if (capturedCells.Count < highlightedCells.Count)
            return;

        // 여기까지 왔다는 것은
        // highlightedCells 안에 있는 모든 셀을 플레이어가 최소 한 번씩 밟았다는 뜻
        Collect();
    }

    /// <summary>
    /// 실제 아이템 획득 처리.
    /// GameManager에 알리고, 필요하면 HP/점수 변경 후 자기 자신을 파괴한다.
    /// </summary>
    private void Collect()
    {
        if (isCollected)
            return;

        isCollected = true;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.CollectItem();

            // 필요 시:
            // GameManager.Instance.AddHp(deltaHp);
            // GameManager.Instance.AddScore(deltaScore);
        }

        Destroy(gameObject);
    }
}
