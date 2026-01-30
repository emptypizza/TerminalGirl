using UnityEngine;
using System.Collections;
using System.Collections.Generic;


[RequireComponent(typeof(Rigidbody2D))]
public class Player : MonoBehaviour
{
    [Header("이동 설정")]
    public float moveSpeed = 5f;

    [Header("플레이어 스탯")]
    public int currentHP = 3;
    public int maxHP = 5;

    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    public bool isMoving = false;
    private bool isKnockback = false;
    private bool isInvincible = false;
    private Vector2Int currentHex;
    private Vector3 lastSafePosition;

    private Coroutine moveCoroutine;
    private Coroutine knockbackCoroutine;

    [Header("넉백 설정")]
    public int knockbackDistance = 0; // 넉백 칸 수 (0: 한 칸만 튕김)

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        rb.isKinematic = true;
    }

    void Start()
    {
        if (GameManager.Instance != null)
        {
            currentHex = GameManager.Instance.GetGridCenter();
            transform.position = HexToWorld(currentHex);
            lastSafePosition = transform.position;
        }
    }

    public void MoveByPath(List<Vector2Int> path)
    {
     

        if (isMoving || path == null || path.Count == 0)
            return;

        moveCoroutine = StartCoroutine(MovePathCoroutine(path));
    }

    private IEnumerator MovePathCoroutine(List<Vector2Int> path)
    {
        isMoving = true;

        // Check if we are starting from a safe position (before trail activation)
        if (GameManager.Instance.CurrentTrail.Count == 0)
        {
            lastSafePosition = transform.position;
        }

        // Activate the starting cell
        GameManager.Instance.GetCellAt(currentHex)?.ActivateTrail();

        try
        {
            foreach (var targetHex in path)
            {
                Cell targetCell = GameManager.Instance.GetCellAt(targetHex);

                // Check for loop closure
                if (targetCell != null && targetCell.currentState == CellState.PlayerTrail)
                {
                    GameManager.Instance.ProcessLoop();
                    break; // Stop movement and end the path traversal
                }

                // --- Movement Lerp ---
                Vector3 startPos = transform.position;
                Vector3 endPos = HexToWorld(targetHex);
                float journey = 0f;
                float moveDuration = 0.2f;

                while (journey < moveDuration)
                {
                    journey += Time.deltaTime;
                    float percent = Mathf.Clamp01(journey / moveDuration);
                    transform.position = Vector3.Lerp(startPos, endPos, percent);
                    yield return null;
                }
                transform.position = endPos;
                // --- End Movement Lerp ---

                currentHex = targetHex;

                // Activate the new cell after moving
                targetCell?.ActivateTrail();

                // Check if we arrived at a safe position (Captured) or if Trail was cleared (Loop closed)
                Cell currentCell = GameManager.Instance.GetCellAt(currentHex);
                if (GameManager.Instance.CurrentTrail.Count == 0 || (currentCell != null && currentCell.currentState == CellState.PlayerCaptured))
                {
                    lastSafePosition = transform.position;
                }
            }
        }
        finally
        {
            // Ensure we snap to grid and reset flags even if interrupted
            transform.position = HexToWorld(currentHex);
            isMoving = false;
        }
    }
    public void ResetStatus(Vector2Int startHex)
    {
        StopAllCoroutines();

        if (moveCoroutine != null)
            StopCoroutine(moveCoroutine);
        if (knockbackCoroutine != null)
            StopCoroutine(knockbackCoroutine);

        moveCoroutine = null;
        knockbackCoroutine = null;

        isMoving = false;
        isKnockback = false;
        isInvincible = false;
        currentHP = 3;

        currentHex = startHex;
        transform.position = HexToWorld(currentHex);
        lastSafePosition = transform.position;

        if (spriteRenderer != null)
            spriteRenderer.enabled = true;

        UIManager.Instance?.UpdateHPUI(currentHP);
    }

    public void TakeDamage(int damage)
    {
        if (isInvincible) return;

        currentHP -= damage;
        SoundManager.Instance?.Play(SoundManager.SoundId.PlayerHit);
        UIManager.Instance?.ShowDamageEffect();
        UIManager.Instance?.UpdateHPUI(currentHP);

        // Knockback to last safe position
        StopAllCoroutines();
        transform.position = lastSafePosition;
        currentHex = WorldToHex(lastSafePosition);
        isMoving = false;
        isKnockback = false;

        // Reset Trail
        GameManager.Instance?.ResetTrail();

        Debug.Log($"HP 감소! 현재 HP: {currentHP}");
        if (currentHP <= 0)
        {
            Debug.Log("게임 오버");
            if (GameManager.Instance != null)
                GameManager.Instance.GameOver();
        }
        else
        {
            StartCoroutine(InvincibilityCoroutine());
        }
    }

    public void GrantStageStartInvincibility(float duration = 1f)
    {
        StartCoroutine(StageStartInvincibilityCoroutine(duration));
    }

    private IEnumerator StageStartInvincibilityCoroutine(float duration)
    {
        isInvincible = true;

        float elapsed = 0f;
        bool visible = true;

        while (elapsed < duration)
        {
            if (spriteRenderer != null)
            {
                visible = !visible;
                spriteRenderer.enabled = visible;
            }
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        if (spriteRenderer != null) spriteRenderer.enabled = true;
        isInvincible = false;
    }

    // Legacy method support if needed, or can be removed if verified unused (Enemy.cs calls TakeDamage now)
    // public void Hit(int damage) => TakeDamage(damage);


    public void Knockback(Vector2Int knockbackDir)
    {
        // [수정] StopAllCoroutines()는 무적 코루틴까지 꺼버리므로 삭제합니다.
        // 대신 이동 관련 코루틴만 개별적으로 정지시킵니다.
        if (moveCoroutine != null) StopCoroutine(moveCoroutine);
        if (knockbackCoroutine != null) StopCoroutine(knockbackCoroutine);

        // Snap to ensure we don't start from an interpolated position
        transform.position = HexToWorld(currentHex);
        isMoving = false; // Reset explicitly before starting new action

        knockbackCoroutine = StartCoroutine(KnockbackCoroutine(knockbackDir));
    }



    /// <summary>
    /// 넉백 1칸만 적용되며 확장 가능성은 유지
    /// </summary>
    private IEnumerator KnockbackCoroutine(Vector2Int knockbackDir)
    {
        isMoving = true;
        isKnockback = true;

        try
        {
            Vector2Int nextHex = currentHex + knockbackDir; // 한 칸만 넉백
            if (!GameManager.Instance.IsCellExists(nextHex))
            {
                Debug.Log("넉백 종료: 맵 바깥");
                yield break;
            }

            Vector3 start = transform.position;
            Vector3 end = HexToWorld(nextHex);
            float duration = 0.27f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                transform.position = Vector3.Lerp(start, end, elapsed / duration);
                elapsed += Time.deltaTime;
                yield return null;
            }

            transform.position = end;
            currentHex = nextHex;
        }
        finally
        {
            // Ensure we snap to grid and reset flags
            transform.position = HexToWorld(currentHex);
            isMoving = false;
            isKnockback = false;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Trash"))
        {
            // 캡처(3칸 점령)용 Trash라면 여기서 아무 것도 하지 않음
         //   if (other.TryGetComponent<TrashSecure>(out var _))
           //     return;

            // '즉시 줍는' 일반 Trash만 기존 처리
            GameManager.Instance.CollectItem();
            Destroy(other.gameObject);
            currentHP = Mathf.Min(currentHP + 1, maxHP);
            UIManager.Instance?.UpdateHPUI(currentHP);
        }
    }

    private IEnumerator InvincibilityCoroutine()
    {
        isInvincible = true;

        float elapsed = 0f;
        bool visible = true;
        while (elapsed < 0.8f)
        {
            if (spriteRenderer != null)
            {
                visible = !visible;
                spriteRenderer.enabled = visible;
            }
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        if (spriteRenderer != null)
            spriteRenderer.enabled = true;

        isInvincible = false;
    }

    #region Coordinate Conversion
    public Vector3 HexToWorld(Vector2Int hex)
    {
        if (GameManager.Instance == null) return Vector3.zero;

        float width = 1f;
        float height = Mathf.Sqrt(3f) / 2f * width;
        Vector2Int gridDim = GameManager.Instance.GetGridDimensions();

        Vector2 offset = new Vector2(
            -width * 0.75f * (gridDim.x - 1) / 2f,
            -height * (gridDim.y - 1) / 2f
        );

        float xPos = width * 0.75f * hex.x + offset.x;
        float yPos = height * (hex.y + 0.5f * (hex.x & 1)) + offset.y;
        return new Vector3(xPos, yPos, 0);
    }

    public Vector2Int WorldToHex(Vector3 worldPos)
    {
        if (GameManager.Instance == null) return Vector2Int.zero;

        float width = 1f;
        float height = Mathf.Sqrt(3f) / 2f * width;
        Vector2Int gridDim = GameManager.Instance.GetGridDimensions();

        Vector2 offset = new Vector2(
            -width * 0.75f * (gridDim.x - 1) / 2f,
            -height * (gridDim.y - 1) / 2f
        );

        float px = worldPos.x - offset.x;
        float py = worldPos.y - offset.y;

        int q = Mathf.RoundToInt(px / (width * 0.75f));
        int r = Mathf.RoundToInt((py - height * 0.5f * (q & 1)) / height);
        return new Vector2Int(q, r);
    }
    #endregion
}
