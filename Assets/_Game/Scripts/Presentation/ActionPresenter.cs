using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SwordGame
{
    /// <summary>顺序消费表现事件（文档 7）：动画只读结果，不再判定伤害。暂停冻结表现时钟。</summary>
    public class ActionPresenter : MonoBehaviour
    {
        [NonSerialized] public float speedMultiplier = 1f;
        [NonSerialized] public bool skipPresentation;
        [NonSerialized] public bool paused;

        private GameConfig _cfg;
        private AudioService _audio;
        private Transform _vfxRoot;
        private Transform _cameraTransform;
        private Vector3 _cameraBasePos;

        private ActorView _playerView;
        private Transform _swordPivot;
        private SwordView _swordView;
        private Dictionary<int, ActorView> _enemyViews = new Dictionary<int, ActorView>();
        private Dictionary<int, IntentView> _intentViews = new Dictionary<int, IntentView>();

        private Transform _previewRoot;
        private readonly List<Vector2[]> _trailScratch = new List<Vector2[]>();
        private int _session;

        public void Init(GameConfig cfg, AudioService audio, Transform vfxRoot, Transform cameraTransform)
        {
            _cfg = cfg;
            _audio = audio;
            _vfxRoot = vfxRoot;
            _cameraTransform = cameraTransform;
            _cameraBasePos = cameraTransform != null ? cameraTransform.position : Vector3.zero;
        }

        /// <summary>换房时更新镜头基准位（震动以此为中心）。</summary>
        public void SetCameraBase(Vector3 pos)
        {
            _cameraBasePos = pos;
            if (_cameraTransform != null)
                _cameraTransform.position = pos;
        }

        public void SetViews(ActorView player, Transform swordPivot, SwordView sword,
            Dictionary<int, ActorView> enemies, Dictionary<int, IntentView> intents)
        {
            _playerView = player;
            _swordPivot = swordPivot;
            _swordView = sword;
            _enemyViews = enemies;
            _intentViews = intents;
        }

        // ---- 行动播放（分段拍事务：玩家阶段 / 敌人阶段 / 生长动画分别驱动）----

        public void PlayBlockedMove(Vector2Int cell, Vector2Int direction, float progress, int sessionId, Action onDone)
        {
            _session = sessionId;
            ClearPreview();
            StartCoroutine(BlockedMoveRoutine(cell, direction, progress, sessionId, onDone));
        }

        private IEnumerator BlockedMoveRoutine(Vector2Int cell, Vector2Int direction, float progress, int session, Action onDone)
        {
            Vector2 from = BoardModel.CellCenter(cell);
            Vector2 contact = from + (Vector2)direction * progress;
            if (!skipPresentation)
            {
                for (int leg = 0; leg < 2; leg++)
                {
                    float elapsed = 0;
                    float duration = (leg == 0 ? .12f : .16f) / Mathf.Max(.01f, speedMultiplier);
                    while (elapsed < duration)
                    {
                        if (_session != session || _playerView == null) yield break;
                        if (paused) { yield return null; continue; }
                        elapsed += Time.deltaTime;
                        float k = EaseOutCubic(Mathf.Clamp01(elapsed / duration));
                        _playerView.transform.position = leg == 0 ? Vector2.Lerp(from, contact, k) : Vector2.Lerp(contact, from, k);
                        yield return null;
                    }
                    if (leg == 0)
                    {
                        _audio?.Play(SfxId.BladeWall);
                        // At zero clearance, give feedback without moving through the obstacle.
                        if (progress <= .001f) _swordView?.FlashSegment(0, .12f);
                    }
                }
            }
            if (_session != session) yield break;
            if (_playerView != null) _playerView.transform.position = from;
            onDone?.Invoke();
        }

        public void PlayPlayerPhase(ActionPresentation p, int sessionId, Action onDone)
        {
            _session = sessionId;
            ClearPreview();
            StartCoroutine(PlayPlayerPhaseRoutine(p, sessionId, onDone));
        }

        private IEnumerator PlayPlayerPhaseRoutine(ActionPresentation p, int session, Action onDone)
        {
            if (skipPresentation)
            {
                SnapToEnd(p);
                onDone?.Invoke();
                yield break;
            }

            switch (p.action.kind)
            {
                case PlayerActionKind.Move:
                    yield return StartCoroutine(PlayMoveRoutine(p, session));
                    break;
                case PlayerActionKind.Rotate:
                    yield return StartCoroutine(PlayRotateRoutine(p, session));
                    break;
                case PlayerActionKind.Wait:
                    yield return StartCoroutine(Wait(0.05f, session));
                    break;
            }
            if (_session != session)
                yield break;
            onDone?.Invoke();
        }

        public void PlayEnemyPhase(ActionPresentation p, int sessionId, Action onDone)
        {
            _session = sessionId;
            StartCoroutine(PlayEnemyPhaseRoutine(p, sessionId, onDone));
        }

        private IEnumerator PlayEnemyPhaseRoutine(ActionPresentation p, int session, Action onDone)
        {
            if (skipPresentation)
            {
                SnapToEnd(p);
                onDone?.Invoke();
                yield break;
            }

            yield return StartCoroutine(PlayEnemyEvents(p, session));
            if (_session != session)
                yield break;

            if (p.outcome == Outcome.Defeat)
            {
                if (_playerView != null)
                    yield return StartCoroutine(_playerView.PlayDeath());
            }

            onDone?.Invoke();
        }

        /// <summary>伸长表现（文档 5.5 / 7.7）：新边从 anchor 向 newNode 从 0 长到 0.5 格，
        /// 按进度 g 消费命中事件；等待最后一个击退表现结束后回调（提交一次）。</summary>
        public void PlayGrowth(GrowthTransactionResult result, int sessionId, Action onDone)
        {
            _session = sessionId;
            StartCoroutine(PlayGrowthRoutine(result, sessionId, onDone));
        }

        private IEnumerator PlayGrowthRoutine(GrowthTransactionResult result, int session, Action onDone)
        {
            if (result == null || !result.valid)
                yield break;

            if (skipPresentation)
            {
                onDone?.Invoke();
                yield break;
            }

            var state = result.nextState;
            Vector2 A = SwordGeometry.HalfToWorld(result.anchorHalf, BoardModel.CellCenter(state.player.cell), state.player.facing);
            Vector2 B = SwordGeometry.HalfToWorld(result.newNodeHalf, BoardModel.CellCenter(state.player.cell), state.player.facing);

            // 伸长中的新增边（提交前真实 SwordView 不包含它，切换时保持同一世界位置）
            var go = new GameObject("GrowingEdge");
            go.transform.SetParent(_vfxRoot, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.SetPosition(0, A);
            lr.SetPosition(1, A);
            lr.startWidth = _cfg.bladeVisualWidth;
            lr.endWidth = _cfg.bladeVisualWidth;
            lr.numCapVertices = 4;
            lr.sharedMaterial = GardenTheme.LineMaterial;
            lr.sortingOrder = 10;
            lr.startColor = _cfg.bladeColor;
            lr.endColor = _cfg.bladeColor;
            var inkGo = new GameObject("GrowthInk");
            inkGo.transform.SetParent(go.transform, false);
            var ink = inkGo.AddComponent<LineRenderer>();
            ink.useWorldSpace = true;
            ink.sharedMaterial = GardenTheme.LineMaterial;
            ink.positionCount = 2;
            ink.SetPosition(0, A); ink.SetPosition(1, A);
            ink.startWidth = ink.endWidth = _cfg.bladeVisualWidth + .04f;
            ink.startColor = ink.endColor = GardenTheme.Ink;
            ink.numCapVertices = 6; ink.sortingOrder = 9;

            _audio?.Play(SfxId.Growth);
            float dur = Mathf.Max(0.01f, _cfg.growthDuration / speedMultiplier);
            float t = 0f;
            int hitIdx = 0;
            var hits = result.hits;
            while (t < dur)
            {
                if (_session != session) { Destroy(go); yield break; }
                if (paused) { yield return null; continue; }
                t += Time.deltaTime;
                float g = EaseOutCubic(Mathf.Clamp01(t / dur));
                lr.SetPosition(1, Vector2.Lerp(A, B, g));
                ink.SetPosition(1, lr.GetPosition(1));
                while (hitIdx < hits.Count && hits[hitIdx].g <= g)
                {
                    TriggerGrowthHit(hits[hitIdx]);
                    hitIdx++;
                }
                yield return null;
            }
            lr.SetPosition(1, B);
            ink.SetPosition(1, B);
            while (hitIdx < hits.Count)
            {
                TriggerGrowthHit(hits[hitIdx]);
                hitIdx++;
            }
            Destroy(go);

            // 等待最后一个击退表现结束，再继续成长 / 恢复敌人阶段（文档 7.7）
            while (_activeKnockbacks > 0)
            {
                if (_session != session) yield break;
                if (paused) { yield return null; continue; }
                yield return null;
            }
            onDone?.Invoke();
        }

        private int _activeKnockbacks;

        private void TriggerGrowthHit(GrowthHitEvent hit)
        {
            if (_enemyViews.TryGetValue(hit.actorId, out var view) && view == null)
                return;

            if (view != null)
            {
                Spark(hit.contactPoint, _cfg.hitSparkColor, 0.35f);
                view.HurtFlash();
                _audio?.Play(hit.kind == EnemyKind.Box ? SfxId.BladeWall : SfxId.HitFlesh);
            }
            else if (hit.isCore)
            {
                Spark(hit.contactPoint, _cfg.hitSparkColor, 0.6f);
                _audio?.Play(SfxId.HitFlesh);
                Shake(0.03f, 0.05f);
            }

            if (view != null && hit.movedByKnock)
            {
                _activeKnockbacks++;
                StartCoroutine(KnockTracked(view, hit.knockFrom, hit.knockTo));
            }
            if (hit.wallImpact)
            {
                var from = BoardModel.CellCenter(hit.knockFrom);
                var to = BoardModel.CellCenter(hit.knockTo);
                Spark((from + to) * 0.5f, _cfg.wallSparkColor, 0.5f);
                Shake(0.03f, 0.05f);
                _audio?.Play(SfxId.KnockWall);
            }
            if (view != null && hit.died)
                StartCoroutine(view.PlayDeath());
        }

        private IEnumerator KnockTracked(ActorView view, Vector2Int fromCell, Vector2Int toCell)
        {
            yield return StartCoroutine(KnockRoutine(view, fromCell, toCell));
            _activeKnockbacks--;
        }

        private IEnumerator PlayMoveRoutine(ActionPresentation p, int session)
        {
            Vector2 from = BoardModel.CellCenter(p.playerFromCell);
            Vector2 to = BoardModel.CellCenter(p.playerToCell);
            if (p.rebound && p.stopReason == StopReason.Wall && p.playerFromCell == p.playerToCell)
            {
                yield return StartCoroutine(BlockedMoveRoutine(p.playerFromCell, p.action.moveDirection,
                    p.blockedMoveProgress, session, null));
                yield break;
            }
            bool fruitTruncated = p.stopReason == StopReason.Fruit;
            Vector2 truncatedTo = from + (to - from) * p.stopU; // 平移砍果：整体到接触进度后归位
            Vector2 target = fruitTruncated ? truncatedTo : to;
            _audio?.Play(SfxId.PlayerMove);

            float baseDur = fruitTruncated ? _cfg.moveDuration * p.stopU : _cfg.moveDuration;
            float dur = Mathf.Max(0.01f, baseDur / speedMultiplier);
            float t = 0f;
            int hitIdx = 0;
            float pendingPause = 0f;
            var hits = p.hits;

            while (t < dur)
            {
                if (_session != session) yield break;
                if (paused) { yield return null; continue; }
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                float u = EaseOutCubic(k) * (fruitTruncated ? p.stopU : 1f);
                if (_playerView != null)
                    _playerView.transform.position = Vector2.Lerp(from, to, u);
                while (hitIdx < hits.Count && hits[hitIdx].u <= u)
                {
                    TriggerHit(hits[hitIdx]);
                    pendingPause += _cfg.hitPausePerTarget;
                    hitIdx++;
                }
                yield return null;
            }

            if (_playerView != null)
                _playerView.transform.position = target;
            while (hitIdx < hits.Count)
            {
                TriggerHit(hits[hitIdx]);
                pendingPause += _cfg.hitPausePerTarget;
                hitIdx++;
            }

            if (p.stopReason == StopReason.Fruit)
            {
                Spark(p.fruitContactPoint, _cfg.hitSparkColor, 0.45f);
                _audio?.Play(SfxId.Growth);
            }
            else if (p.bodyPickup && _playerView != null)
            {
                Spark(p.playerToCell == Vector2Int.zero ? from : BoardModel.CellCenter(p.playerToCell), _cfg.hitSparkColor, 0.45f);
                _audio?.Play(SfxId.Growth);
            }

            if (pendingPause > 0f)
                yield return StartCoroutine(Wait(Mathf.Min(pendingPause, _cfg.hitPauseCap), session));

            if (fruitTruncated)
            {
                // 平移砍果：人与剑整体沿原路径回到出发格（文档 4.4）
                float returnDur = Mathf.Max(0.06f, dur * _cfg.returnScale) / speedMultiplier;
                t = 0f;
                while (t < returnDur)
                {
                    if (_session != session) yield break;
                    if (paused) { yield return null; continue; }
                    t += Time.deltaTime;
                    float k = Mathf.Clamp01(t / returnDur);
                    float back = 1f - EaseInOutQuad(k);
                    if (_playerView != null)
                        _playerView.transform.position = Vector2.Lerp(target, from, back);
                    yield return null;
                }
                if (_playerView != null)
                    _playerView.transform.position = from;
                yield return StartCoroutine(Wait(_cfg.wallPause, session));
                yield break;
            }

            if (_playerView != null)
                _playerView.transform.position = to;
            yield return StartCoroutine(Wait(_cfg.knockbackDuration, session));
        }

        private IEnumerator PlayRotateRoutine(ActionPresentation p, int session)
        {
            if (_swordPivot == null)
                yield break;

            float startAngle = p.startFacing * 90f;
            _audio?.Play(SfxId.BladeSwing);

            if (p.isZeroSweep)
            {
                // 零扫角：只播接触反馈（文档 7.2）
                bool enemyBlock = p.stopReason == StopReason.Enemy && p.hits.Count > 0;
                if (enemyBlock)
                    TriggerHit(p.hits[0]);
                if (_swordView != null)
                    _swordView.FlashSegment(enemyBlock ? p.hits[0].segmentIndex : p.wallSegmentIndex, 0.08f);
                Spark(enemyBlock ? p.hits[0].contactPoint : p.wallContactPoint,
                    enemyBlock ? _cfg.hitSparkColor : _cfg.wallSparkColor, 0.5f);
                Shake(0.03f, 0.05f);
                _audio?.Play(SfxId.BladeWall);
                yield return StartCoroutine(Wait(_cfg.wallPause, session));
                _swordPivot.localRotation = Quaternion.Euler(0f, 0f, p.endFacing * 90f);
                yield break;
            }

            float stopU = p.stopReason == StopReason.None ? 1f : p.stopU;
            float delta = p.action.quarterTurns * 90f * stopU;
            float outDur = Mathf.Max(0.02f, _cfg.rotateDuration * stopU / speedMultiplier);
            float t = 0f;
            int hitIdx = 0;
            float pendingPause = 0f;
            var hits = p.hits;

            while (t < outDur)
            {
                if (_session != session) yield break;
                if (paused) { yield return null; continue; }
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / outDur);
                float eased = EaseOutCubic(k);
                float progress = stopU * eased;
                _swordPivot.localRotation = Quaternion.Euler(0f, 0f, startAngle + delta * eased);
                while (hitIdx < hits.Count && hits[hitIdx].u <= progress)
                {
                    TriggerHit(hits[hitIdx]);
                    pendingPause += _cfg.hitPausePerTarget;
                    hitIdx++;
                }
                yield return null;
            }
            while (hitIdx < hits.Count)
            {
                TriggerHit(hits[hitIdx]);
                pendingPause += _cfg.hitPausePerTarget;
                hitIdx++;
            }

            if (pendingPause > 0f)
                yield return StartCoroutine(Wait(Mathf.Min(pendingPause, _cfg.hitPauseCap), session));

            if (p.stopReason != StopReason.None)
            {
                bool isFruit = p.stopReason == StopReason.Fruit;
                bool isEnemy = p.stopReason == StopReason.Enemy;
                var blocker = isEnemy && p.hits.Count > 0 ? p.hits[p.hits.Count - 1] : default;
                if (_swordView != null)
                    _swordView.FlashSegment(isFruit ? 0 : isEnemy ? blocker.segmentIndex : p.wallSegmentIndex, 0.08f);
                Spark(isFruit ? p.fruitContactPoint : isEnemy ? blocker.contactPoint : p.wallContactPoint,
                    isFruit || isEnemy ? _cfg.hitSparkColor : _cfg.wallSparkColor, 0.5f);
                Shake(0.04f, 0.06f);
                _audio?.Play(isFruit ? SfxId.Growth : SfxId.BladeWall);
                yield return StartCoroutine(Wait(_cfg.wallPause, session));

                float returnDur = Mathf.Max(0.06f, outDur * _cfg.returnScale) / speedMultiplier;
                t = 0f;
                while (t < returnDur)
                {
                    if (_session != session) yield break;
                    if (paused) { yield return null; continue; }
                    t += Time.deltaTime;
                    float k = Mathf.Clamp01(t / returnDur);
                    float back = 1f - EaseInOutQuad(k);
                    _swordPivot.localRotation = Quaternion.Euler(0f, 0f, startAngle + delta * back);
                    yield return null;
                }
            }

            _swordPivot.localRotation = Quaternion.Euler(0f, 0f, p.endFacing * 90f);
        }

        private void TriggerHit(HitEvent hit)
        {
            if (_enemyViews.TryGetValue(hit.actorId, out var view) && view != null)
            {
                Spark(hit.contactPoint, _cfg.hitSparkColor, 0.35f);
                view.HurtFlash();
                _audio?.Play(hit.kind == EnemyKind.Box ? SfxId.BladeWall : SfxId.HitFlesh);

                if (hit.movedByKnock)
                    StartCoroutine(KnockRoutine(view, hit.knockFrom, hit.knockTo));
                if (hit.wallImpact)
                {
                    Spark(BoardModel.CellCenter(hit.knockFrom) + (BoardModel.CellCenter(hit.knockTo) - BoardModel.CellCenter(hit.knockFrom)) * 0.5f,
                        _cfg.wallSparkColor, 0.5f);
                    Shake(0.03f, 0.05f);
                    _audio?.Play(SfxId.KnockWall);
                }
                if (hit.died)
                    StartCoroutine(view.PlayDeath());
            }
            else if (hit.isCore)
            {
                Spark(hit.contactPoint, _cfg.hitSparkColor, 0.6f);
                _audio?.Play(SfxId.HitFlesh);
                Shake(0.03f, 0.05f);
            }
        }

        private IEnumerator KnockRoutine(ActorView view, Vector2Int fromCell, Vector2Int toCell)
        {
            Vector2 from = BoardModel.CellCenter(fromCell);
            Vector2 to = BoardModel.CellCenter(toCell);
            float dur = Mathf.Max(0.01f, _cfg.knockbackDuration / speedMultiplier);
            float t = 0f;
            while (t < dur)
            {
                if (view == null) yield break;
                if (paused) { yield return null; continue; }
                t += Time.deltaTime;
                float k = EaseOutCubic(Mathf.Clamp01(t / dur));
                view.transform.position = Vector2.Lerp(from, to, k);
                yield return null;
            }
            if (view != null)
                view.transform.position = to;
        }

        private IEnumerator PlayEnemyEvents(ActionPresentation p, int session)
        {
            foreach (var evt in p.enemyEvents)
            {
                if (_session != session) yield break;
                switch (evt.kind)
                {
                    case EnemyEventKind.Move:
                        if (_enemyViews.TryGetValue(evt.actorId, out var mv) && mv != null)
                            yield return StartCoroutine(MoveActorRoutine(mv, BoardModel.CellCenter(evt.fromCell), BoardModel.CellCenter(evt.toCell), _cfg.enemyMoveDuration, session));
                        _audio?.Play(SfxId.EnemyMove);
                        break;
                    case EnemyEventKind.AttackMove:
                        if (_enemyViews.TryGetValue(evt.actorId, out var av) && av != null)
                            av.Flash(new Color(1f, 0.4f, 0.2f), 0.12f);
                        HurtPlayer();
                        break;
                    case EnemyEventKind.ShootLine:
                    case EnemyEventKind.CorePulse:
                        yield return StartCoroutine(RayFlashRoutine(evt.rayCells, session));
                        _audio?.Play(SfxId.EnemyShoot);
                        if (evt.playerDamaged)
                            HurtPlayer();
                        break;
                    case EnemyEventKind.Aim:
                        if (_enemyViews.TryGetValue(evt.actorId, out var cv) && cv != null)
                            cv.Flash(new Color(1f, 0.5f, 0.2f, 0.9f), 0.08f);
                        break;
                }
                yield return StartCoroutine(Wait(0.04f, session));
            }
        }

        private void HurtPlayer()
        {
            _playerView?.HurtFlash(0.15f);
            Shake(0.04f, 0.06f);
            _audio?.Play(SfxId.Hurt);
        }

        private IEnumerator MoveActorRoutine(ActorView view, Vector2 from, Vector2 to, float duration, int session)
        {
            float dur = Mathf.Max(0.01f, duration / speedMultiplier);
            float t = 0f;
            while (t < dur)
            {
                if (_session != session || view == null) yield break;
                if (paused) { yield return null; continue; }
                t += Time.deltaTime;
                float k = EaseInOutQuad(Mathf.Clamp01(t / dur));
                view.transform.position = Vector2.Lerp(from, to, k);
                yield return null;
            }
            if (view != null)
                view.transform.position = to;
        }

        private IEnumerator RayFlashRoutine(List<Vector2Int> cells, int session)
        {
            if (cells == null || cells.Count == 0)
                yield break;
            var flashes = new List<SpriteRenderer>();
            foreach (var cell in cells)
            {
                var sr = MakeFlashQuad(BoardModel.CellCenter(cell), 0.9f, _cfg.intentRayColor);
                flashes.Add(sr);
            }
            float dur = Mathf.Max(0.01f, _cfg.arrowFlashDuration / speedMultiplier);
            float t = 0f;
            while (t < dur)
            {
                if (_session != session) { Cleanup(); yield break; }
                if (paused) { yield return null; continue; }
                t += Time.deltaTime;
                float a = 1f - t / dur;
                foreach (var sr in flashes)
                    if (sr != null)
                        sr.color = new Color(_cfg.intentRayColor.r, _cfg.intentRayColor.g, _cfg.intentRayColor.b, a * 0.7f);
                yield return null;
            }
            Cleanup();

            void Cleanup()
            {
                foreach (var sr in flashes)
                    if (sr != null)
                        Destroy(sr.gameObject);
            }
        }

        // ---- VFX ----

        private SpriteRenderer MakeFlashQuad(Vector2 pos, float size, Color color)
        {
            var go = new GameObject("Vfx");
            go.transform.SetParent(_vfxRoot, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.SoftCircle();
            sr.color = color;
            sr.sortingOrder = 20;
            float baseSize = sr.sprite.bounds.size.x;
            if (baseSize > 0.001f)
                go.transform.localScale = Vector3.one * (size / baseSize);
            go.transform.position = pos;
            return sr;
        }

        private void Spark(Vector2 pos, Color color, float size)
        {
            if (_vfxRoot == null) return;
            StartCoroutine(SparkRoutine(pos, color, size));
        }

        private IEnumerator SparkRoutine(Vector2 pos, Color color, float size)
        {
            var sr = MakeFlashQuad(pos, size * 0.4f, color);
            float dur = 0.15f / speedMultiplier;
            float t = 0f;
            while (t < dur)
            {
                if (_session != 0 && sr == null) yield break;
                if (paused) { yield return null; continue; }
                t += Time.deltaTime;
                float k = t / dur;
                if (sr != null)
                {
                    sr.transform.localScale = Vector3.one * ((size * (0.4f + 0.8f * k)) / sr.sprite.bounds.size.x);
                    var c = color;
                    c.a = 1f - k;
                    sr.color = c;
                }
                yield return null;
            }
            if (sr != null)
                Destroy(sr.gameObject);
        }

        private void Shake(float amplitude, float duration)
        {
            if (_cameraTransform == null) return;
            StartCoroutine(ShakeRoutine(amplitude, duration));
        }

        private IEnumerator ShakeRoutine(float amplitude, float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                if (paused) { yield return null; continue; }
                t += Time.deltaTime;
                if (_cameraTransform != null)
                {
                    var offset = new Vector3(
                        (Mathf.Sin(Time.time * 73f) * amplitude),
                        (Mathf.Cos(Time.time * 61f) * amplitude), 0f);
                    _cameraTransform.position = _cameraBasePos + offset;
                }
                yield return null;
            }
            if (_cameraTransform != null)
                _cameraTransform.position = _cameraBasePos;
        }

        private IEnumerator Wait(float seconds, int session)
        {
            float dur = Mathf.Max(0.001f, seconds / speedMultiplier);
            float t = 0f;
            while (t < dur)
            {
                if (_session != session) yield break;
                if (paused) { yield return null; continue; }
                t += Time.deltaTime;
                yield return null;
            }
        }

        private void SnapToEnd(ActionPresentation p)
        {
            if (_playerView != null)
                _playerView.transform.position = BoardModel.CellCenter(p.playerToCell);
            if (_swordPivot != null)
                _swordPivot.localRotation = Quaternion.Euler(0f, 0f, p.endFacing * 90f);
        }

        private static float EaseOutCubic(float k) => 1f - Mathf.Pow(1f - k, 3f);
        private static float EaseInOutQuad(float k) => k < 0.5f ? 2f * k * k : 1f - Mathf.Pow(-2f * k + 2f, 2f) / 2f;

        // ---- 行动预览（文档 7.5）：与实际共用同一结算结果 ----

        public void ShowPreview(ActionPresentation p, Vector2Int playerCell)
        {
            ClearPreview();
            if (p == null || !p.isValid)
                return;
            if (p.action.kind == PlayerActionKind.Wait)
                return;

            if (_previewRoot == null)
            {
                _previewRoot = new GameObject("PreviewRoot").transform;
                _previewRoot.SetParent(_vfxRoot, false);
            }

            var segments = SwordGeometry.GetSegments(GetSwordState());
            int segCount = segments.Count;
            _trailScratch.Clear();
            for (int i = 0; i < segCount; i++)
                _trailScratch.Add(new Vector2[9]);

            float startRad = p.startFacing * Mathf.PI / 2f;
            float wallU = p.rotateBlocked ? p.wallU : 1f;

            for (int s = 0; s <= 8; s++)
            {
                float u = wallU * s / 8f;
                Vector2 pivot;
                float angle;
                if (p.action.kind == PlayerActionKind.Move)
                {
                    pivot = BoardModel.CellCenter(p.playerFromCell) + new Vector2(p.action.moveDirection.x, p.action.moveDirection.y) * u;
                    angle = startRad;
                }
                else
                {
                    pivot = BoardModel.CellCenter(p.playerFromCell);
                    angle = startRad + p.action.quarterTurns * Mathf.PI / 2f * u;
                }
                SwordView.SampleWorldSegments(segments, pivot, angle, GetScratchA(s), GetScratchB(s), segCount);
            }

            for (int i = 0; i < segCount; i++)
            {
                var lr = MakeTrailLine(p.action.kind == PlayerActionKind.Move ? startRad : -1f);
                lr.positionCount = 9;
                for (int s = 0; s <= 8; s++)
                    lr.SetPosition(s, _trailScratch[i][s]);
            }

            if (p.rotateBlocked)
            {
                // 回弹轨迹（颜色稍暗）
                for (int s = 0; s <= 8; s++)
                {
                    float u = wallU * (1f - s / 8f);
                    float angle = startRad + p.action.quarterTurns * Mathf.PI / 2f * u;
                    SwordView.SampleWorldSegments(segments, BoardModel.CellCenter(p.playerFromCell), angle, GetScratchA(s), GetScratchB(s), segCount);
                }
                for (int i = 0; i < segCount; i++)
                {
                    var lr = MakeTrailLine(-2f);
                    lr.positionCount = 9;
                    for (int s = 0; s <= 8; s++)
                        lr.SetPosition(s, _trailScratch[i][s]);
                }
                var markerGo = new GameObject("ContactMarker");
                markerGo.transform.SetParent(_previewRoot.transform, false);
                var marker = markerGo.AddComponent<SpriteRenderer>();
                marker.sprite = SpriteFactory.Diamond();
                marker.color = Color.white;
                marker.sortingOrder = 21;
                markerGo.transform.position = p.wallContactPoint;
                markerGo.transform.localScale = Vector3.one * (0.3f / marker.sprite.bounds.size.x);
            }

            foreach (var hit in p.hits)
            {
                if (_enemyViews.TryGetValue(hit.actorId, out var view) && view != null)
                {
                    var ringGo = new GameObject($"HitRing_{hit.actorId}");
                    ringGo.transform.SetParent(_previewRoot.transform, false);
                    var ring = ringGo.AddComponent<SpriteRenderer>();
                    ring.sprite = GardenTheme.Ring();
                    ring.color = GardenTheme.Coral;
                    ring.sortingOrder = -2;
                    ringGo.transform.position = BoardModel.CellCenter(view.CurrentCell());
                    ringGo.transform.localScale = Vector3.one * (0.85f / ring.sprite.bounds.size.x);
                }
            }
        }

        private Vector2[] GetScratchA(int s)
        {
            while (_trailScratchA.Count <= s) _trailScratchA.Add(new Vector2[10]);
            return _trailScratchA[s];
        }
        private Vector2[] GetScratchB(int s)
        {
            while (_trailScratchB.Count <= s) _trailScratchB.Add(new Vector2[10]);
            return _trailScratchB[s];
        }
        private readonly List<Vector2[]> _trailScratchA = new List<Vector2[]>();
        private readonly List<Vector2[]> _trailScratchB = new List<Vector2[]>();

        private LineRenderer MakeTrailLine(float mode)
        {
            var go = new GameObject("Trail");
            go.transform.SetParent(_previewRoot.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.widthMultiplier = 0.05f;
            lr.numCapVertices = 2;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.sortingOrder = 9;
            lr.startColor = mode == -2f ? _cfg.trailReturnColor : _cfg.trailOutColor;
            lr.endColor = mode == -2f ? _cfg.trailReturnColor : _cfg.trailOutColor;
            return lr;
        }

        private SwordState GetSwordState()
        {
            return _swordStateProvider != null ? _swordStateProvider() : new SwordState();
        }

        public void SetSwordStateProvider(Func<SwordState> provider) => _swordStateProvider = provider;
        private Func<SwordState> _swordStateProvider;

        public void ClearPreview()
        {
            if (_previewRoot != null)
                Destroy(_previewRoot);
            _previewRoot = null;
        }

        public void SetCurrentCellAccessor(Func<int, Vector2Int> accessor) => _enemyCellAccessor = accessor;
        private Func<int, Vector2Int> _enemyCellAccessor;
    }

    public static class ActorViewExtensions
    {
        public static Vector2Int CurrentCell(this ActorView view)
        {
            var p = view.transform.position;
            return new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));
        }
    }
}
