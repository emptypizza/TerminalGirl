// ItemSpawner_Grid.cs
// 아이템을 특정 조건(점수, 시간 등)에 따라 Hex Grid 내 무작위 셀에 생성하는 스포너

using System.Collections.Generic;
using UnityEngine;

public class ItemSpawner_Grid : MonoBehaviour
{
    [Header("아이템 프리팹")]
    public GameObject itemPrefab;

    [Header("스폰 주기 (초)")]
    public float spawnInterval = 5f;

    [Header("스폰 조건")]
    public int requiredScore = 3;
    public float requiredTime = 10f;

    private float nextSpawnTime = 0f;
    private bool paused = false;


    public int spawnCount = 3;


    void Update()
    {
        if (paused)
            return;

        int gridWidth = GameManager.Instance.gridWidth;
        int gridHeight = GameManager.Instance.gridHeight;
        Vector2Int playerHex = GameManager.Instance.player.WorldToHex(GameManager.Instance.player.transform.position);

        // 1. 전체 셀 좌표 리스트 생성
        List<Vector2Int> availableCells = new List<Vector2Int>();
        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (cell == playerHex)
                    continue;
                availableCells.Add(cell);
            }
        }

        // 2. (선택) 이미 아이템 존재 셀 제외 로직 필요시 여기에 구현


        int idx = Random.Range(0, availableCells.Count);
        Vector2Int chosenCell = availableCells[idx];
        availableCells.RemoveAt(idx);

        Vector3 worldPos = GameManager.Instance.player.HexToWorld(chosenCell);


        if (Time.time >= nextSpawnTime && ShouldSpawnItem())
        {
            GameObject item = Instantiate(itemPrefab, worldPos, Quaternion.identity);

            nextSpawnTime = Time.time + spawnInterval;
        }

       // foreach (GameObject a in GameManager.Instance._itemlist_field)
        //    GameManager.Instance.HighlightNeighborsAroundWorld(worldPos, clearBefore: true);


        
    }

    public void PauseSpawning()
    {
        paused = true;
    }

    public void ResumeSpawningWithDelay(float delaySeconds)
    {
        nextSpawnTime = Time.time + Mathf.Max(0f, delaySeconds);
        paused = false;
    }

    private bool ShouldSpawnItem()
    {
    
        if (GameManager.Instance == null)
            return false;

        return GameManager.Instance.CurrentScore >= requiredScore && GameManager.Instance.GameTime >= requiredTime;
    }
  
    /// <summary>
    /// 그리드 내 랜덤 셀에 아이템을 중복 없이, 플레이어 위치 제외하고 생성
    /// </summary>
    private void PaintItem()
    {
     
       // _itemlist_field

       
           
    }




}
