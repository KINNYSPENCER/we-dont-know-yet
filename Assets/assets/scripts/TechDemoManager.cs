using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class TechDemoManager : MonoBehaviour
{
    private enum Turn { Player, Enemy }

    [Header("Movement Settings")]
    public Transform playerTransform;
    public float moveSpeed = 5f;

    [Header("Action Points")]
    public int maxActionPoints = 3;
    [Tooltip("Tiles you can travel for 1 AP. Cost = distance / this, rounded up.")]
    public float tilesPerAP = 2f;
    [Tooltip("AP spent on one attack roll (player and enemy).")]
    public int attackCost = 1;
    [Tooltip("Raise this if the highlight tiles are hidden behind your ground sprites.")]
    public int highlightSortingOrder = 1;

    [Header("Controls")]
    public KeyCode moveKey = KeyCode.Space;
    public KeyCode interactKey = KeyCode.E;
    public KeyCode endTurnKey = KeyCode.Return;
    public KeyCode restartKey = KeyCode.R;

    [Header("Camera")]
    public bool cameraFollowsPlayer = true;
    [Tooltip("Higher = snappier. Around 5-10 feels smooth.")]
    public float cameraFollowSpeed = 6f;

    [Header("Player Stats")]
    public int maxPlayerHP = 3;
    public int playerAC = 12;
    [Range(1, 10)] public int playerStat = 5;

    [Header("Enemy Stats")]
    public int maxEnemyHP = 3;
    public int enemyAC = 10;
    public int enemyStat = 0;
    [Tooltip("Logs the enemy's hidden d6 to the Console so you can test the AI.")]
    public bool debugLogEnemyRoll = true;

    [Header("References")]
    public Transform targetObjectTransform; // urban if you see this, this is the enemy
    public SpriteRenderer diceSpriteRenderer;
    public Sprite[] d20Faces; // Elements 0-19: Faces 1-20. Elements 20+: Blur/Rolling sprites!
    public float interactionRadius = 1.2f;

    [Header("Attacks (click the enemy to open the menu)")]
    public Sprite lightAttackIcon;   // drag the framed sword icon here
    public Sprite heavyAttackIcon;   // drag the big red sword icon here
    public int lightAttackCost = 1;
    public int lightAttackDamage = 1;
    public int lightAttackHitMod = 0;
    public int heavyAttackCost = 3;
    public int heavyAttackDamage = 2;
    public int heavyAttackHitMod = -1;
    public float menuButtonSize = 80f;

    [Header("Heal (click your player to open the menu)")]
    public Sprite healIcon;
    public int healCost = 1;
    public int healAmount = 1;
    [Tooltip("Player can heal once every N of their turns. 2 = use it, skip one turn, usable again the turn after.")]
    public int healCooldownTurns = 2;

    [Header("Fireball (long range spell, used by player and enemy)")]
    public Sprite fireballIcon;
    public int fireballCost = 2;
    public int fireballDamage = 2;
    [Tooltip("Added to the d20 roll. The enemy also adds its own enemyStat.")]
    public int fireballHitMod = 0;
    [Tooltip("Max distance in tiles. Normal attacks need to be adjacent.")]
    public float fireballRange = 6f;
    [Tooltip("Player can cast fireball once every N of their turns. 2 = cast it, skip one turn, usable again the turn after. Does not affect the enemy.")]
    public int fireballCooldownTurns = 2;

    [Header("Shield (uses your whole turn)")]
    public Sprite shieldIcon;
    public KeyCode shieldKey = KeyCode.Q;
    [Tooltip("AC bonus against enemy attacks during the enemy turn after you shield.")]
    public int shieldACBonus = 4;
    [Tooltip("Extra AP you earn on your next turn for spending this turn shielding.")]
    public int shieldBonusAP = 1;

    // Turn / game state
    private Turn turn = Turn.Player;
    private bool gameOver = false;
    private string endText = "";
    private int playerHP, enemyHP;
    private int currentAP, enemyAP;
    private string enemyMessage = "";

    // Player ability cooldowns (in player turns)
    private int fireballCooldownRemaining = 0;
    private int healCooldownRemaining = 0;

    // Movement state
    private Vector3 targetPosition;
    private bool isMoving = false;
    private bool moveModeActive = false;
    private bool attackMenuOpen = false;
    private bool healMenuOpen = false;
    private bool shielded = false;
    private int bonusAPNext = 0;
    private int hoverCost = 0;
    private Vector3 playerStart, enemyStart;

    // Range  visuals
    private Camera cam;
    private Sprite squareSprite;
    private Transform highlightRoot;
    private SpriteRenderer hoverMarker;
    private readonly List<SpriteRenderer> highlightPool = new List<SpriteRenderer>();

    // 1 AP = blue, 2 AP = yellow, 3+ AP = orange
    private static readonly Color[] CostColors =
    {
        new Color(0.25f, 0.60f, 1.00f, 0.35f),
        new Color(1.00f, 0.85f, 0.20f, 0.35f),
        new Color(1.00f, 0.50f, 0.15f, 0.35f)
    };

    // Dice state
    private bool hasRolled = false;
    private bool isWin = false;
    private bool isRolling = false;
    private int lastD20Roll = 0;
    private string rollText = "";

    // GUI
    private GUIStyle labelStyle, boxStyle, resultStyle, smallStyle;



    void Start()
    {
        if (targetObjectTransform == null)
        {
            Debug.LogError("TechDemoManager: assign the enemy to 'Target Object Transform'.");
            enabled = false;
            return;
        }

        cam = Camera.main;
        squareSprite = CreateSquareSprite();

        // Snap both onto the grid so highlights line up
        playerTransform.position = SnapToGrid(playerTransform.position);
        targetObjectTransform.position = SnapToGrid(targetObjectTransform.position);
        playerStart = playerTransform.position;
        enemyStart = targetObjectTransform.position;

        highlightRoot = new GameObject("RangeHighlights").transform;
        hoverMarker = CreateTile("HoverMarker", highlightSortingOrder + 1);
        hoverMarker.color = new Color(1f, 1f, 1f, 0.55f);

        if (targetObjectTransform.GetComponent<SpriteRenderer>() == null)
        {
            SpriteRenderer sr = targetObjectTransform.gameObject.AddComponent<SpriteRenderer>();
            sr.sprite = squareSprite;
            sr.color = Color.red;
        }

        ResetGame();
    }

    void ResetGame()
    {
        StopAllCoroutines();

        gameOver = false;
        endText = "";
        enemyMessage = "";
        rollText = "";
        hasRolled = false;
        isRolling = false;
        isMoving = false;
        attackMenuOpen = false;
        healMenuOpen = false;
        shielded = false;
        bonusAPNext = 0;
        fireballCooldownRemaining = 0;
        healCooldownRemaining = 0;
        turn = Turn.Player;
        HideDice();

        playerHP = maxPlayerHP;
        enemyHP = maxEnemyHP;
        currentAP = maxActionPoints;
        enemyAP = 0;

        playerTransform.position = playerStart;
        targetPosition = playerStart;
        targetObjectTransform.position = enemyStart;
        targetObjectTransform.gameObject.SetActive(true);

        SnapCameraToPlayer();
        SetMoveMode(false);
    }

    // ----------------------------------------------------------------- Update

    void Update()
    {
        if (gameOver)
        {
            if (Input.GetKeyDown(restartKey)) ResetGame();
            return;
        }

        if (turn != Turn.Player || isRolling) return;

        if (isMoving)
        {
            MovePlayer();
            return;
        }

        if (Input.GetKeyDown(moveKey))
        {
            if (moveModeActive) SetMoveMode(false);
            else if (currentAP > 0) SetMoveMode(true);
        }

        if (Input.GetKeyDown(KeyCode.Escape)) SetMoveMode(false);
        if (Input.GetKeyDown(shieldKey)) { UseShield(); return; }
        if (Input.GetKeyDown(endTurnKey)) { currentAP = 0; StartEnemyTurn(); return; }

        if (moveModeActive) HandleMoveSelection();

        HandlePlayerAttack();
    }

    // ----------------------------------------------------------------- Camera

    void LateUpdate()
    {
        if (cam != null && playerTransform != null && cameraFollowsPlayer)
        {
            Vector3 goal = new Vector3(playerTransform.position.x, playerTransform.position.y, cam.transform.position.z);
            float t = 1f - Mathf.Exp(-cameraFollowSpeed * Time.deltaTime);
            cam.transform.position = Vector3.Lerp(cam.transform.position, goal, t);
        }

        KeepDiceOnScreen();
    }

    // Pins the dice to the center of the camera view so it never gets left behind.
    void KeepDiceOnScreen()
    {
        if (diceSpriteRenderer == null || cam == null) return;
        if (!diceSpriteRenderer.gameObject.activeSelf) return;

        Vector3 c = cam.transform.position;
        diceSpriteRenderer.transform.position = new Vector3(c.x, c.y, 0f);
        diceSpriteRenderer.sortingOrder = 100; // draw above everything
    }

    void SnapCameraToPlayer()
    {
        if (cam == null) return;
        cam.transform.position = new Vector3(playerTransform.position.x, playerTransform.position.y, cam.transform.position.z);
    }

    // ------------------------------------------------------------ Turn system

    // Out of AP -> enemy's turn.
    void AfterPlayerAction()
    {
        if (!gameOver && currentAP <= 0) StartEnemyTurn();
    }

    void StartEnemyTurn()
    {
        attackMenuOpen = false;
        healMenuOpen = false;
        SetMoveMode(false);
        HideDice();
        hasRolled = false;
        turn = Turn.Enemy;
        StartCoroutine(EnemyTurnRoutine());
    }

    void HideDice()
    {
        if (diceSpriteRenderer != null)
            diceSpriteRenderer.gameObject.SetActive(false);
    }

    IEnumerator EnemyTurnRoutine()
    {
        enemyAP = maxActionPoints;
        enemyMessage = "";
        yield return new WaitForSeconds(0.6f);


        int d6 = Random.Range(1, 7);
        bool approach = d6 % 2 == 0;
        if (debugLogEnemyRoll)
            Debug.Log("Enemy d6: " + d6 + (approach ? " (even: approach)" : " (odd: retreat)"));

        Vector2Int origin = ToTile(targetObjectTransform.position);
        Vector2Int dest = ChooseEnemyTile(approach);

        if (dest != origin)
        {
            enemyAP -= GetMoveCost(origin, dest);
            yield return MoveEnemyTo(new Vector3(dest.x, dest.y, 0f));
            yield return new WaitForSeconds(0.3f);
        }


        if (approach)
        {
            while (enemyAP >= attackCost && EnemyIsAdjacent() && !gameOver)
            {
                enemyAP -= attackCost;
                EnemyAttack();
                yield return new WaitForSeconds(0.9f);
            }
        }

        // Out of melee reach but in range: throw fireballs with whatever AP is left
        while (enemyAP >= fireballCost && !EnemyIsAdjacent() && EnemyInFireballRange() && !gameOver)
        {
            enemyAP -= fireballCost;
            EnemyFireball();
            yield return new WaitForSeconds(0.9f);
        }

        if (!gameOver)
        {
            enemyAP = 0;
            turn = Turn.Player;
            currentAP = maxActionPoints + bonusAPNext; // shield earns bonus AP
            bonusAPNext = 0;
            shielded = false;

            // A new player turn begins: tick player ability cooldowns down
            fireballCooldownRemaining = Mathf.Max(0, fireballCooldownRemaining - 1);
            healCooldownRemaining = Mathf.Max(0, healCooldownRemaining - 1);
        }
    }

    // ------------------------------------------------------------ stupid enemy

    Vector2Int ChooseEnemyTile(bool toward)
    {
        Vector2Int origin = ToTile(targetObjectTransform.position);
        Vector2Int playerTile = ToTile(playerTransform.position);
        int radius = Mathf.CeilToInt(enemyAP * tilesPerAP);

        Vector2Int best = origin;
        float bestDist = Vector2.Distance(origin, playerTile);
        int bestCost = 0;

        for (int x = -radius; x <= radius; x++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                Vector2Int tile = origin + new Vector2Int(x, y);
                int cost = GetMoveCost(origin, tile);
                if (cost < 1 || cost > enemyAP) continue;
                if (tile == playerTile) continue; // Can't stand on the player

                float d = Vector2.Distance(tile, playerTile);
                bool better = toward ? d < bestDist - 0.001f : d > bestDist + 0.001f;
                bool tie = Mathf.Abs(d - bestDist) <= 0.001f && cost < bestCost;

                if (better || tie)
                {
                    best = tile;
                    bestDist = d;
                    bestCost = cost;
                }
            }
        }

        return best;
    }

    IEnumerator MoveEnemyTo(Vector3 dest)
    {
        Transform e = targetObjectTransform;
        while (Vector3.Distance(e.position, dest) > 0.01f)
        {
            e.position = Vector3.MoveTowards(e.position, dest, moveSpeed * Time.deltaTime);
            yield return null;
        }
        e.position = dest;
    }

    bool EnemyIsAdjacent()
    {
        return Vector2.Distance(targetObjectTransform.position, playerTransform.position) <= interactionRadius;
    }

    bool EnemyInFireballRange()
    {
        return Vector2.Distance(targetObjectTransform.position, playerTransform.position) <= fireballRange;
    }

    void EnemyFireball()
    {
        int ac = playerAC + (shielded ? shieldACBonus : 0);
        int total = Random.Range(1, 21) + enemyStat + fireballHitMod;
        bool hit = total >= ac;
        enemyMessage = "Enemy casts FIREBALL! Rolls " + total + " vs AC " + ac + (shielded ? " (SHIELDED)" : "") + ": " + (hit ? "HIT" : "MISS");

        if (hit)
        {
            playerHP = Mathf.Max(0, playerHP - fireballDamage);
            if (playerHP <= 0)
            {
                gameOver = true;
                endText = "YOU DIED";
            }
        }
    }

    void EnemyAttack()
    {
        int ac = playerAC + (shielded ? shieldACBonus : 0);
        int total = Random.Range(1, 21) + enemyStat;
        bool hit = total >= ac;
        enemyMessage = "Enemy rolls " + total + " vs AC " + ac + (shielded ? " (SHIELDED)" : "") + ": " + (hit ? "HIT" : "MISS");

        if (hit)
        {
            playerHP = Mathf.Max(0, playerHP - 1);
            if (playerHP <= 0)
            {
                gameOver = true;
                endText = "YOU DIED";
            }
        }
    }

    // ------------------------------------------------------------ Player move

    void HandleMoveSelection()
    {
        Vector2Int origin = ToTile(playerTransform.position);
        Vector2Int hovered = ToTile(cam.ScreenToWorldPoint(Input.mousePosition));

        int cost = GetMoveCost(origin, hovered);
        bool valid = cost >= 1 && cost <= currentAP && !IsEnemyTile(hovered);

        hoverCost = valid ? cost : 0;
        hoverMarker.gameObject.SetActive(valid);
        if (valid) hoverMarker.transform.position = new Vector3(hovered.x, hovered.y, 0f);

        if (Input.GetMouseButtonDown(1)) SetMoveMode(false); // right click cancel moving

        if (Input.GetMouseButtonDown(0) && valid && !MouseOverShieldButton())
        {
            currentAP -= cost;
            targetPosition = new Vector3(hovered.x, hovered.y, 0f);
            isMoving = true;
            SetMoveMode(false);
        }
    }

    void MovePlayer()
    {
        playerTransform.position = Vector3.MoveTowards(playerTransform.position, targetPosition, moveSpeed * Time.deltaTime);

        if (Vector3.Distance(playerTransform.position, targetPosition) < 0.01f)
        {
            playerTransform.position = targetPosition;
            isMoving = false;
            AfterPlayerAction();
        }
    }

    // ------------------------------------------------------------ Cost to move

    int GetMoveCost(Vector2Int from, Vector2Int to)
    {
        float dist = Vector2.Distance(from, to);
        if (dist < 0.5f) return 0; // Same tile
        return Mathf.CeilToInt(dist / tilesPerAP);
    }

    bool IsEnemyTile(Vector2Int tile)
    {
        return targetObjectTransform.gameObject.activeSelf && ToTile(targetObjectTransform.position) == tile;
    }

    void SetMoveMode(bool on)
    {
        moveModeActive = on;
        if (on) { attackMenuOpen = false; healMenuOpen = false; }
        hoverCost = 0;
        hoverMarker.gameObject.SetActive(false);

        if (on) ShowRange();
        else HideRange();
    }

    void ShowRange()
    {
        Vector2Int origin = ToTile(playerTransform.position);
        int radius = Mathf.CeilToInt(currentAP * tilesPerAP);
        int used = 0;

        for (int x = -radius; x <= radius; x++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                Vector2Int tile = origin + new Vector2Int(x, y);
                int cost = GetMoveCost(origin, tile);
                if (cost < 1 || cost > currentAP) continue;
                if (IsEnemyTile(tile)) continue;

                SpriteRenderer sr = GetHighlight(used++);
                sr.transform.position = new Vector3(tile.x, tile.y, 0f);
                sr.color = CostColors[Mathf.Clamp(cost - 1, 0, CostColors.Length - 1)];
                sr.gameObject.SetActive(true);
            }
        }

        for (int i = used; i < highlightPool.Count; i++)
            highlightPool[i].gameObject.SetActive(false);
    }

    void HideRange()
    {
        for (int i = 0; i < highlightPool.Count; i++)
            highlightPool[i].gameObject.SetActive(false);
    }

    SpriteRenderer GetHighlight(int index)
    {
        while (highlightPool.Count <= index)
            highlightPool.Add(CreateTile("RangeTile", highlightSortingOrder));
        return highlightPool[index];
    }

    SpriteRenderer CreateTile(string tileName, int sortingOrder)
    {
        GameObject go = new GameObject(tileName);
        go.transform.SetParent(highlightRoot);
        go.transform.localScale = Vector3.one * 0.94f; // gap between tile
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = squareSprite;
        sr.sortingOrder = sortingOrder;
        go.SetActive(false);
        return sr;
    }

    Sprite CreateSquareSprite()
    {
        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        tex.filterMode = FilterMode.Point;
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f); // 1 pixel = 1 tile
    }

    Vector2Int ToTile(Vector3 worldPos)
    {
        return new Vector2Int(Mathf.RoundToInt(worldPos.x), Mathf.RoundToInt(worldPos.y));
    }

    Vector3 SnapToGrid(Vector3 pos)
    {
        return new Vector3(Mathf.Round(pos.x), Mathf.Round(pos.y), 0f);
    }

    // ------------------------------------------------------------ Player attack

    // ------------------------------------------------------------ Shield

    void UseShield()
    {
        if (turn != Turn.Player || isRolling || isMoving || gameOver) return;

        shielded = true;
        bonusAPNext = shieldBonusAP;
        currentAP = 0;            // shielding takes the whole turn: no attacking
        attackMenuOpen = false;
        StartEnemyTurn();
    }

    Rect GetShieldRect()
    {
        return new Rect(20, 150, menuButtonSize, menuButtonSize);
    }

    bool MouseOverShieldButton()
    {
        Vector2 m = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        return GetShieldRect().Contains(m);
    }

    void DrawShieldButton()
    {
        if (turn != Turn.Player || isRolling || isMoving || gameOver) return;

        Rect r = GetShieldRect();
        bool clicked = GUI.Button(r, shieldIcon == null ? "Shield" : "");
        DrawIcon(r, shieldIcon, true);

        string cap = "SHIELD (" + shieldKey + ")\n+" + shieldACBonus + " AC, +" + shieldBonusAP + " AP next turn\nends your turn, no attacking";
        GUI.Box(new Rect(r.xMax + 8, r.y, 230, r.height), cap, smallStyle);

        if (clicked) UseShield();
    }

    bool CanAttack(int cost)
    {
        if (shielded) return false;
        if (!targetObjectTransform.gameObject.activeSelf) return false;
        float distance = Vector2.Distance(playerTransform.position, targetObjectTransform.position);
        return distance <= interactionRadius && currentAP >= cost;
    }

    void StartAttack(int cost, int damage, int hitMod, bool isFireball = false)
    {
        currentAP -= cost;
        if (isFireball) fireballCooldownRemaining = fireballCooldownTurns;
        attackMenuOpen = false;
        SetMoveMode(false);
        StartCoroutine(AnimateDiceRollRoutine(damage, hitMod));
    }

    void HandlePlayerAttack()
    {
        if (!targetObjectTransform.gameObject.activeSelf)
        {
            attackMenuOpen = false;
            return;
        }

        // E = quick light attack (kept from before)
        if (Input.GetKeyDown(interactKey) && CanAttack(lightAttackCost))
        {
            StartAttack(lightAttackCost, lightAttackDamage, lightAttackHitMod);
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
        {
            attackMenuOpen = false;
            healMenuOpen = false;
        }

        // Click the enemy to open/close the attack menu, or yourself for the heal menu
        if (!moveModeActive && Input.GetMouseButtonDown(0))
        {
            if (attackMenuOpen && MouseOverAttackMenu()) return; // the buttons handle it in OnGUI
            if (healMenuOpen && MouseOverHealMenu()) return;
            if (MouseOverShieldButton()) return;

            Vector2Int clicked = ToTile(cam.ScreenToWorldPoint(Input.mousePosition));
            bool onEnemy = IsEnemyTile(clicked);
            bool onPlayer = clicked == ToTile(playerTransform.position);

            attackMenuOpen = onEnemy ? !attackMenuOpen : false;
            healMenuOpen = onPlayer ? !healMenuOpen : false;
        }
    }

    // ------------------------------------------------------------ Heal

    bool CanHeal()
    {
        return playerHP < maxPlayerHP && currentAP >= healCost && healCooldownRemaining <= 0;
    }

    void StartHeal()
    {
        if (!CanHeal()) return;

        currentAP -= healCost;
        playerHP = Mathf.Min(maxPlayerHP, playerHP + healAmount);
        healCooldownRemaining = healCooldownTurns;
        healMenuOpen = false;
        SetMoveMode(false);
        AfterPlayerAction();
    }

    Rect GetHealRect()
    {
        Vector3 sp = cam.WorldToScreenPoint(playerTransform.position + Vector3.up * 0.8f);
        float size = menuButtonSize;
        return new Rect(sp.x - size / 2f, Screen.height - sp.y - size - 46f, size, size);
    }

    bool MouseOverHealMenu()
    {
        Vector2 m = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        return GetHealRect().Contains(m);
    }

    void DrawHealMenu()
    {
        if (!healMenuOpen || turn != Turn.Player || isRolling || isMoving || gameOver) return;
        if (cam == null) return;

        Rect r = GetHealRect();
        bool ok = CanHeal();

        GUI.enabled = ok;
        bool clicked = GUI.Button(r, healIcon == null ? "Heal" : "");
        GUI.enabled = true;

        DrawIcon(r, healIcon, ok);
        string cap = healCooldownRemaining > 0
            ? "Cooldown\n" + healCooldownRemaining + (healCooldownRemaining == 1 ? " turn" : " turns")
            : healCost + " AP\n+" + healAmount + " HP" + (playerHP >= maxPlayerHP ? " (full)" : "");
        GUI.Box(new Rect(r.center.x - 55f, r.yMax + 2f, 110f, 42f), cap, smallStyle);

        if (clicked) StartHeal();
    }

    void GetMenuRects(out Rect light, out Rect heavy, out Rect fire)
    {
        Vector3 sp = cam.WorldToScreenPoint(targetObjectTransform.position + Vector3.up * 0.8f);
        float top = Screen.height - sp.y - 14f;      // top of the enemy's stat box
        float size = menuButtonSize, gap = 40f;
        float y = top - size - 46f;                  // leaves room for the captions
        heavy = new Rect(sp.x - size / 2f, y, size, size);
        light = new Rect(heavy.x - size - gap, y, size, size);
        fire = new Rect(heavy.xMax + gap, y, size, size);
    }

    bool MouseOverAttackMenu()
    {
        Rect light, heavy, fire;
        GetMenuRects(out light, out heavy, out fire);
        Vector2 m = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        return light.Contains(m) || heavy.Contains(m) || fire.Contains(m);
    }

    bool CanCastFireball()
    {
        if (shielded) return false;
        if (fireballCooldownRemaining > 0) return false;
        if (!targetObjectTransform.gameObject.activeSelf) return false;
        float distance = Vector2.Distance(playerTransform.position, targetObjectTransform.position);
        return distance <= fireballRange && currentAP >= fireballCost;
    }

    void DrawIcon(Rect r, Sprite s, bool enabled)
    {
        if (s == null) return;
        Texture2D t = s.texture;
        Rect tr = s.textureRect;
        Rect uv = new Rect(tr.x / t.width, tr.y / t.height, tr.width / t.width, tr.height / t.height);
        GUI.color = enabled ? Color.white : new Color(1f, 1f, 1f, 0.35f);
        GUI.DrawTextureWithTexCoords(new Rect(r.x + 6, r.y + 6, r.width - 12, r.height - 12), t, uv);
        GUI.color = Color.white;
    }

    void DrawAttackButton(Rect r, Sprite icon, string fallbackLabel, string caption, int cost, int damage, int hitMod, bool ok, bool isFireball = false)
    {
        GUI.enabled = ok;
        bool clicked = GUI.Button(r, icon == null ? fallbackLabel : "");
        GUI.enabled = true;

        DrawIcon(r, icon, ok);
        GUI.Box(new Rect(r.center.x - 55f, r.yMax + 2f, 110f, 42f), caption, smallStyle);

        if (clicked) StartAttack(cost, damage, hitMod, isFireball);
    }

    void DrawAttackMenu()
    {
        if (!attackMenuOpen || turn != Turn.Player || isRolling || isMoving || gameOver) return;
        if (cam == null || !targetObjectTransform.gameObject.activeSelf) return;

        Rect light, heavy, fire;
        GetMenuRects(out light, out heavy, out fire);

        string lightCap = lightAttackCost + " AP\n" + lightAttackDamage + " dmg";
        string heavyCap = heavyAttackCost + " AP\n" + heavyAttackDamage + " dmg, " +
                          (heavyAttackHitMod >= 0 ? "+" : "") + heavyAttackHitMod + " hit";

        string fireCap = fireballCooldownRemaining > 0
            ? "Cooldown\n" + fireballCooldownRemaining + (fireballCooldownRemaining == 1 ? " turn" : " turns")
            : fireballCost + " AP\n" + fireballDamage + " dmg, range " + fireballRange;

        DrawAttackButton(light, lightAttackIcon, "Light", lightCap, lightAttackCost, lightAttackDamage, lightAttackHitMod, CanAttack(lightAttackCost));
        DrawAttackButton(heavy, heavyAttackIcon, "Heavy", heavyCap, heavyAttackCost, heavyAttackDamage, heavyAttackHitMod, CanAttack(heavyAttackCost));
        DrawAttackButton(fire, fireballIcon, "Fireball", fireCap, fireballCost, fireballDamage, fireballHitMod, CanCastFireball(), true);
    }

    IEnumerator AnimateDiceRollRoutine(int damage, int hitMod)
    {
        isRolling = true;
        hasRolled = false;
        enemyMessage = "";

        bool hasFaces = d20Faces != null && d20Faces.Length >= 20;

        if (diceSpriteRenderer != null && hasFaces)
        {
            diceSpriteRenderer.gameObject.SetActive(true);

            float rollTimer = 0f;
            float rollDuration = 0.25f;

            while (rollTimer < rollDuration)
            {
                rollTimer += Time.deltaTime;

                int index = d20Faces.Length > 20
                    ? Random.Range(20, d20Faces.Length)
                    : Random.Range(0, 20);
                diceSpriteRenderer.sprite = d20Faces[index];

                yield return new WaitForSeconds(0.04f);
            }
        }

        // final roll
        lastD20Roll = Random.Range(1, 21);
        int totalResult = lastD20Roll + playerStat + hitMod;

        if (diceSpriteRenderer != null && hasFaces)
            diceSpriteRenderer.sprite = d20Faces[lastD20Roll - 1];

        isWin = totalResult >= enemyAC;
        rollText = totalResult + " vs AC " + enemyAC;

        if (isWin)
        {
            enemyHP = Mathf.Max(0, enemyHP - damage);
            if (enemyHP <= 0)
            {
                gameOver = true;
                endText = "ENEMY DEFEATED";
                targetObjectTransform.gameObject.SetActive(false);
            }
        }

        hasRolled = true;

        // If this attack used the last AP, the turn is about to end and hide the dice.
        // Hold on the result briefly so the player can actually see it.
        if (!gameOver && currentAP <= 0)
            yield return new WaitForSeconds(1.0f);

        isRolling = false;

        AfterPlayerAction();
    }

    // --------------------------------------------------------------------- GUI

    void EnsureStyles()
    {
        if (labelStyle != null) return;

        labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
        labelStyle.normal.textColor = Color.white;

        smallStyle = new GUIStyle(GUI.skin.box) { fontSize = 14, alignment = TextAnchor.MiddleCenter };

        boxStyle = new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.MiddleCenter };

        resultStyle = new GUIStyle(GUI.skin.box) { fontSize = 24, alignment = TextAnchor.MiddleCenter };
    }

    void DrawPips(float x, float y, int filled, int total, Color color)
    {
        for (int i = 0; i < total; i++)
        {
            GUI.color = i < filled ? color : new Color(1f, 1f, 1f, 0.25f);
            GUI.DrawTexture(new Rect(x + i * 34, y, 28, 28), Texture2D.whiteTexture);
        }
        GUI.color = Color.white;
    }

    void OnGUI()
    {
        EnsureStyles();

        // points/hud
        GUI.Label(new Rect(20, 14, 220, 26), "ACTION POINTS", labelStyle);
        DrawPips(20, 42, currentAP, Mathf.Max(maxActionPoints, currentAP), new Color(0.3f, 0.8f, 1f));

        GUI.Label(new Rect(20, 80, 220, 26), "HP   (AC " + (playerAC + (shielded ? shieldACBonus : 0)) + ")", labelStyle);
        DrawPips(20, 108, playerHP, maxPlayerHP, new Color(0.9f, 0.25f, 0.25f));

        // turn
        GUI.Label(new Rect(Screen.width / 2 - 100, 14, 200, 30),
            turn == Turn.Player ? "YOUR TURN" : "ENEMY TURN", labelStyle);

        // enemy stats
        if (cam != null && targetObjectTransform != null && targetObjectTransform.gameObject.activeSelf)
        {
            Vector3 sp = cam.WorldToScreenPoint(targetObjectTransform.position + Vector3.up * 0.8f);
            string t = "HP " + enemyHP + "/" + maxEnemyHP + "  AC " + enemyAC;
            if (turn == Turn.Enemy) t += "  AP " + enemyAP;
            GUI.Box(new Rect(sp.x - 85, Screen.height - sp.y - 14, 170, 26), t, smallStyle);
        }

        DrawAttackMenu();
        DrawHealMenu();
        DrawShieldButton();

        // Walk cost
        if (moveModeActive && hoverCost > 0)
        {
            Vector2 m = Event.current.mousePosition;
            GUI.Box(new Rect(m.x + 16, m.y - 36, 80, 28), hoverCost + " AP", boxStyle);
        }

        // Controls
        string hint = moveModeActive
            ? "Click a highlighted tile to move   |   " + moveKey + " / Right-click: cancel"
            : moveKey + ": move   |   Click enemy: attack menu (fireball works at range)   |   Click yourself: heal   |   " + interactKey + ": quick light attack   |   " + shieldKey + ": shield   |   " + endTurnKey + ": end turn";
        GUI.Label(new Rect(20, Screen.height - 36, Screen.width - 40, 26), hint, labelStyle);


        if (isRolling)
        {
            resultStyle.normal.textColor = Color.yellow;
            GUI.Box(new Rect(Screen.width / 2 - 180, 80, 360, 60), "ROLLING...", resultStyle);
        }
        else if (hasRolled)
        {
            resultStyle.normal.textColor = isWin ? Color.green : Color.red;
            GUI.Box(new Rect(Screen.width / 2 - 180, 80, 360, 60), (isWin ? "WIN" : "MISS") + "  (" + rollText + ")", resultStyle);
        }


        if (!string.IsNullOrEmpty(enemyMessage))
            GUI.Box(new Rect(Screen.width / 2 - 210, 150, 420, 40), enemyMessage, boxStyle);


        if (gameOver)
        {
            resultStyle.normal.textColor = Color.white;
            GUI.Box(new Rect(Screen.width / 2 - 230, Screen.height / 2 - 50, 460, 100),
                endText + "\nPress " + restartKey + " to restart", resultStyle);
        }
    }
}