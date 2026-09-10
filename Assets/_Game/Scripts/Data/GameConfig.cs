using UnityEngine;

namespace SwordGame
{
    /// <summary>GameConfig 初始参数（文档 17.1）+ 美术引用。只读资产，运行时不写回。</summary>
    [CreateAssetMenu(fileName = "CFG_Game_Default", menuName = "SwordGame/Game Config")]
    public sealed class GameConfig : ScriptableObject
    {
        [Header("几何")]
        public float tileSize = 1.0f;
        public float playerRadius = 0.28f;
        public float enemyRadius = 0.30f;
        public float coreRadius = 0.40f;
        public float bladeRadius = 0.10f;
        public float bladeVisualWidth = 0.22f;

        [Header("成长")]
        public float growthHoverRadius = 0.12f;    // 鼠标到候选中心线的最大吸附距离
        public float growthNodeDeadZone = 0.08f;   // 已有结点中心的无选择区半径
        public float fruitRadius = 0.20f;          // 剑与果接触圆

        [Header("战斗数值")]
        public int playerHp = 4;
        public int enemyHp = 2;
        public int coreHp = 3;
        public int bladeDamage = 1;
        public int wallImpactDamage = 1;
        public int enemyDamage = 1;
        public int knockbackCells = 1;
        public int maxEnemiesPerRoom = 6;

        [Header("求解器")]
        public float outerMotionStep = 0.10f;
        public float contactTolerance = 0.002f;
        public float numericalEpsilon = 0.00001f;

        [Header("表现时长（秒）")]
        public float moveDuration = 0.18f;
        public float rotateDuration = 0.20f;
        public float wallPause = 0.05f;
        public float returnScale = 0.60f;
        public float knockbackDuration = 0.10f;
        public float enemyMoveDuration = 0.14f;
        public float growthDuration = 0.45f;
        public float hitPausePerTarget = 0.035f;
        public float hitPauseCap = 0.06f;
        public float arrowFlashDuration = 0.10f;

        [Header("美术引用")]
        public Sprite playerSprite;
        public Sprite chargerSprite;
        public Sprite archerSprite;
        public Sprite coreSprite;
        public Sprite wallSprite;
        public Sprite floorSprite;
        public Sprite fruitSprite;
        public Sprite exitSprite;
        public Sprite boxSprite;
        public Sprite pressurePlateSprite;
        public TMPro.TMP_FontAsset uiFont;

        [Header("配色")]
        public Color backgroundColor = new Color(0.07f, 0.06f, 0.09f);
        public Color floorTint = new Color(0.75f, 0.72f, 0.70f);
        public Color wallTint = Color.white;
        public Color bladeColor = new Color(0.92f, 0.95f, 1.00f);
        public Color bladeFlashColor = Color.white;
        public Color trailOutColor = new Color(1f, 1f, 1f, 0.35f);
        public Color trailReturnColor = new Color(1f, 1f, 1f, 0.18f);
        public Color intentArrowColor = new Color(1.0f, 0.42f, 0.10f);
        public Color intentRayColor = new Color(1.0f, 0.30f, 0.10f, 0.55f);
        public Color exitLitColor = new Color(0.35f, 1.0f, 0.55f);
        public Color exitDimColor = new Color(0.30f, 0.32f, 0.35f);
        public Color hitSparkColor = new Color(1f, 0.9f, 0.4f);
        public Color wallSparkColor = new Color(1f, 1f, 1f);

        public float EnemyRadiusFor(EnemyKind kind) => kind == EnemyKind.Core ? coreRadius : enemyRadius;
        public int HpFor(EnemyKind kind) => kind == EnemyKind.Core ? coreHp : kind == EnemyKind.Box ? 999 : enemyHp;
    }
}
