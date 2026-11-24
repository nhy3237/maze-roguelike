using System.Collections.Generic;
using RingingLoot.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RingingLoot.View
{
    /// 임시 뷰: 씬 설정 없이 Play만 누르면 라운드 1 맵(시드 고정)과 플레이어가 나온다.
    /// 규칙은 전부 Core에 있고 여기는 입력 읽기·그리기·카메라·임시 안내문만 한다.
    /// 그림은 임시 색 사각형이다. 실제 스프라이트(PPU 36)는 이후 단계에서 교체한다.
    public sealed class GameBootstrap : MonoBehaviour
    {
        const uint DebugSeed = 1;          // 웹 v2b에서 같은 시드로 비교할 때 쓴다 (첫 판). 다시 하기(R)는 같은 맵, 새 맵(N)은 무작위 시드
        static uint seed = DebugSeed;
        static int bestScore; static readonly List<string> sessionRuns = new();
        const float CameraSize = 6f;       // 임시값(세로 반 높이, 칸). 기획 확인 필요

        GameMap map;
        PlayerState player;
        TorchSystem torch;
        Visibility vis;
        RelicSystem relics;
        TrapSystem traps;
        GuardSystem guards;
        RunState run; Transform root;
        readonly List<(float at, string text)> pending = new();
        bool recorded, newBest; Vector2 logScroll; string copyMsg = "";
        SpriteRenderer guardBody, eyeL, eyeR, grabRing;
        // 아트(스프라이트 PNG). SpriteLibrary가 없으면(에디터가 아직 안 만든 경우) 예전 색 사각형으로 그린다.
        SpriteLibrary lib; bool art, guardArt; Sprite white;
        SpriteRenderer playerSr, shadowP, shadowG, flame, aura; SpriteRenderer[] stars = new SpriteRenderer[2];
        float flameBase; bool moved;
        static readonly int[] WalkCycle = { 1, 0, 2, 0 };       // 걷기: 왼발 → 서기 → 오른발 → 서기 (warrior_hood_*_0 은 서 있는 자세)
        bool hintedEyes;
        float guardStepT, heartT, rumbleT; bool collapseSfx;   // 효과음 타이머(v2b: stepT, heartT, rumbleTimer)
        // 방울 소리 표현(임시): 코드로 만든 짧은 톤 + 소리가 닿는 범위(칸)까지 퍼지는 고리
        AudioSource audioSrc; Sprite ringSprite;
        readonly List<(SpriteRenderer sr, float t0, float radius)> rings = new();
        const float RingSeconds = 0.7f;
        SpriteRenderer[,] tiles;
        readonly List<(SpriteRenderer sr, int r, int c, Color col, string kind, int idx)> items = new();
        readonly Dictionary<RelicState, (SpriteRenderer body, SpriteRenderer spark)> relicViews = new();
        Camera cam;
        string toast = ""; float toastUntil; bool hintedSpark;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindFirstObjectByType<GameBootstrap>() != null) return;
            new GameObject("GameBootstrap").AddComponent<GameBootstrap>();
        }

        void Start()
        {
            root = new GameObject("World").transform;
            var rows = ObjectRandomizer.Randomize(GameMap.FixedMapV1, RoundConfig.Round(1), seed);
            Debug.Log($"[RingingLoot] round 1 seed {seed}\n" + string.Join("\n", rows));
            map = new GameMap(rows);
            player = PlayerState.AtSpawn(map);                // v2b: 입구 바로 옆 칸에서 시작
            torch = new TorchSystem(map);
            vis = new Visibility(map);
            relics = new RelicSystem(map, vis, RelicDefs.Round1());
            traps = new TrapSystem(map);
            guards = new GuardSystem(map, RoundConfig.Round(1).Patrol, 1.5);
            run = new RunState(RoundConfig.Round(1), relics, $"시드 {seed}");      // 붕괴 130초, 탈출·사망·로그 (v2b CONFIG.collapse)

            var sprite = MakeWhiteSprite(); white = sprite;
            lib = Resources.Load<SpriteLibrary>("SpriteLibrary");
            art = lib != null && lib.Get("Tiles/fl0_00") != null;
            if (!art) Debug.LogWarning("[RingingLoot] SpriteLibrary가 없어 색 사각형으로 그립니다. 메뉴 Tools > Build Sprite Library 를 실행하세요.");
            tiles = new SpriteRenderer[map.Rows, map.Cols];
            for (int r = 0; r < map.Rows; r++)
                for (int c = 0; c < map.Cols; c++)
                    tiles[r, c] = Make(art ? "Tiles/" + TileGridData.Names[r][c] : null, c, r, 1f, Color.white, 0).GetComponent<SpriteRenderer>();
            AddItem("Props/exit_portal", map.Start.r, map.Start.c, 1f, new Color(0.15f, 0.40f, 0.40f), 1, "start", 0);
            for (int i = 0; i < traps.Traps.Count; i++) AddItem("Traps/trap_idle", traps.Traps[i].R, traps.Traps[i].C, 0.7f, new Color(0.2f, 0.25f, 0.25f), 2, "trap", i);
            for (int i = 0; i < torch.Oils.Count; i++) AddItem("Props/oil_bottle", torch.Oils[i].R, torch.Oils[i].C, 0.4f, new Color(0.9f, 0.8f, 0.2f), 2, "oil", i);
            foreach (var rl in relics.Relics)
            {
                var body = Make("Relics/" + rl.Def.Id, rl.C, rl.R, 0.5f, Color.white, 3).GetComponent<SpriteRenderer>();
                var spark = Make("Props/sparkle", rl.C, rl.R, 0.16f, new Color(1f, 0.94f, 0.75f), 4).GetComponent<SpriteRenderer>();
                relicViews[rl] = (body, spark);
            }

            shadowP = Quad(sprite, 0, 0, 1f, new Color(0f, 0f, 0f, 0.4f), 3).GetComponent<SpriteRenderer>();     // 발밑 그림자(v2b: 24x4px)
            shadowG = Quad(sprite, 0, 0, 1f, new Color(0f, 0f, 0f, 0.4f), 3).GetComponent<SpriteRenderer>();
            shadowP.transform.localScale = shadowG.transform.localScale = new Vector3(24f / 36f, 4f / 36f, 1f);
            guardBody = Make("Characters/guardian_patrol", 0, 0, 0.7f, Color.white, 4).GetComponent<SpriteRenderer>();
            guardArt = guardBody.sprite != white;
            eyeL = Quad(sprite, 0, 0, 0.1f, new Color(1f, 0.1f, 0.1f), 5).GetComponent<SpriteRenderer>();
            eyeR = Quad(sprite, 0, 0, 0.1f, new Color(1f, 0.1f, 0.1f), 5).GetComponent<SpriteRenderer>();
            grabRing = Quad(sprite, 0, 0, 1f, Color.white, 6).GetComponent<SpriteRenderer>();
            for (int i = 0; i < stars.Length; i++) stars[i] = Make("Props/sparkle", 0, 0, 0.1f, new Color(1f, 0.91f, 0.69f), 7).GetComponent<SpriteRenderer>();
            playerSr = Make("Characters/warrior_hood_down_0", 0, 0, (float)(PlayerMover.Radius * 2), new Color(0.95f, 0.75f, 0.3f), 5).GetComponent<SpriteRenderer>();
            flame = Make("Props/torch_flame", 0, 0, 0.2f, new Color(1f, 0.55f, 0.2f), 6).GetComponent<SpriteRenderer>();
            flameBase = flame.transform.localScale.x;

            cam = Camera.main;
            if (cam == null) { var go = new GameObject("Main Camera"); go.tag = "MainCamera"; cam = go.AddComponent<Camera>(); }
            cam.orthographic = true; cam.orthographicSize = CameraSize; cam.backgroundColor = Color.black;
            cam.clearFlags = CameraClearFlags.SolidColor;
            if (FindFirstObjectByType<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
            audioSrc = gameObject.AddComponent<AudioSource>(); audioSrc.playOnAwake = false;
            ringSprite = MakeRingSprite();
            aura = Quad(ringSprite, 0, 0, 1.02f, new Color(0.31f, 0.82f, 0.77f, 0.3f), 6).GetComponent<SpriteRenderer>();   // 봉인의 조각을 들고 있으면 몸 둘레에 청록 고리
            Sync();
        }

        void Update()
        {
            var kb = Keyboard.current; var ms = Mouse.current;
            if (kb != null && kb.mKey.wasPressedThisFrame) { Sfx.On = !Sfx.On; Say(Sfx.On ? "소리 켬" : "소리 끔"); }
            if (kb == null) return;
            double dt = Mathf.Min(Time.deltaTime, 0.05f);

            if (run.Phase != RunPhase.Play)                   // 끝난 판: 화면만 유지하고 R/N만 받는다
            {
                RecordResult();
                moved = false;
                Paint(); Sync();
                if (kb.rKey.wasPressedThisFrame) Restart(seed);
                if (kb.nKey.wasPressedThisFrame) Restart((uint)Random.Range(1, 1000000));
                return;
            }
            for (int i = pending.Count - 1; i >= 0; i--)      // 붕괴 안내는 획득 0.9초 뒤(v2b)
                if (Time.time >= pending[i].at) { Say(pending[i].text, 2.6f); pending.RemoveAt(i); }

            // 마우스 월드 좌표(웹 좌표: y 아래로 증가)
            bool inside = false; double mx = 0, my = 0;
            if (ms != null)
            {
                Vector2 sp = ms.position.ReadValue();
                inside = sp.x >= 0 && sp.y >= 0 && sp.x <= Screen.width && sp.y <= Screen.height;
                var w = cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, Mathf.Abs(cam.transform.position.z)));
                mx = w.x; my = -w.y;
            }
            // 제작자 결정(11-20): 마우스 위치는 줍기 클릭과 던지는 방향에만 쓴다. 캐릭터는 이동 방향을 보므로 UpdateAim(마우스 조준)은 부르지 않는다.

            // 입력: F 횃불, 마우스 좌클릭 줍기·탈출(본 유물만), 우클릭/1·2·3 던지기
            if (kb.fKey.wasPressedThisFrame) torch.Toggle();
            if (ms != null && inside)
            {
                if (ms.leftButton.wasPressedThisFrame) OnClick(mx, my);
                if (ms.rightButton.wasPressedThisFrame) relics.ThrowRelic(player, mx, my);
            }
            if (kb.digit1Key.wasPressedThisFrame) OnSlot(1, inside, mx, my);
            if (kb.digit2Key.wasPressedThisFrame) OnSlot(2, inside, mx, my);
            if (kb.digit3Key.wasPressedThisFrame) OnSlot(3, inside, mx, my);

            if (run.Phase != RunPhase.Play) { Paint(); Sync(); return; }   // 이번 프레임 입력으로 탈출함

            double ax = 0, ay = 0;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) ax -= 1;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) ax += 1;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) ay -= 1;     // 웹 좌표: 위쪽이 y 감소
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) ay += 1;

            relics.Tick(dt);                                  // v2b 순서: 무적 → 함정 → 이동 → 함정 피격 → 횃불 → 방울 → (수호자) → 시야
            traps.Update(dt, relics.Level);
            double px0 = player.X, py0 = player.Y;
            PlayerMover.Move(map, player, ax, ay, dt);
            moved = System.Math.Abs(player.X - px0) + System.Math.Abs(player.Y - py0) > 1e-6;
            CheckTrapHit();
            torch.Update(dt, player);
            relics.UpdateRings(dt, player);
            guards.Update(dt, moved, player, torch, relics, traps);   // 소리 이벤트(torch/relics)를 여기서 읽고, 아래 ShowEvents에서 비운다
            run.OnTorchEvents(torch.Events, torch.Fuel);      // 로그·붕괴·사망은 이벤트를 비우기 전에 읽는다
            run.OnRelicEvents(relics.Events);
            run.OnGuardEvents(guards.Events, player);
            run.Update(dt, player, guards);
            foreach (var n in run.Notices) pending.Add((Time.time + (float)n.delay, n.text));
            run.Notices.Clear();
            vis.Compute(player, torch.Light);
            relics.MarkSeen(torch.Light);
            ShowEvents();
            AmbientSfx(dt);
            UpdateRings();
            Paint();
            Sync();
        }

        void RecordResult()
        {
            if (recorded) return;
            recorded = true;
            newBest = run.FinalScore > bestScore;
            if (newBest) bestScore = run.FinalScore;
            Sfx.Play(audioSrc, run.Phase == RunPhase.Escaped ? "escape" : "death");
            sessionRuns.Add($"#{sessionRuns.Count + 1} R1 {run.OutcomeText()} {run.FinalScore}점 {RunState.FmtTime(run.Time)}");
            Debug.Log("[RingingLoot] 판 로그\n" + FullLog());    // 화면에는 안 보여 주므로 콘솔(에디터)에 남깁니다
        }

        void Restart(uint nextSeed)
        {
            seed = nextSeed;
            Destroy(root.gameObject);                         // 이 판에서 만든 그림 전부(World 아래)
            new GameObject("GameBootstrap").AddComponent<GameBootstrap>();
            Destroy(gameObject);
        }

        // 방울이 울림: 소리 + 고리. 소리는 닿는 범위(방울 radius 7칸) 안에서 가까울수록 크게 들린다. 수호자 반응은 7단계.
        void OnBell(RelicEvent e)
        {
            float radius = (float)e.Relic.Def.Rings.Radius;
            double d = System.Math.Sqrt(System.Math.Pow(e.X - player.X, 2) + System.Math.Pow(e.Y - player.Y, 2));
            Sfx.Play(audioSrc, "bell", Mathf.Max(0.25f, 1f - (float)d / 12f));     // v2b: 멀리 떨어진 방울은 작게(고리는 항상 표시)
            var go = new GameObject("bellRing"); go.transform.SetParent(root, false);
            go.transform.position = new Vector3((float)e.X, -(float)e.Y, 0f);
            var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = ringSprite; sr.sortingOrder = 6;
            rings.Add((sr, Time.time, radius));
        }

        void UpdateRings()
        {
            for (int i = rings.Count - 1; i >= 0; i--)
            {
                var (sr, t0, radius) = rings[i];
                float u = (Time.time - t0) / RingSeconds;
                if (u >= 1f) { Destroy(sr.gameObject); rings.RemoveAt(i); continue; }
                float s = 2f * radius * Mathf.Max(u, 0.05f);              // 지름 = 2 × 반경
                sr.transform.localScale = new Vector3(s, s, 1f);
                sr.color = new Color(1f, 0.92f, 0.6f, 0.65f * (1f - u));
            }
        }

        static Sprite MakeRingSprite()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f)) / (size / 2f);
                    float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.94f) / 0.06f);                // 가는 고리
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);   // 지름 1유닛
        }

        // 같은 원인의 안내가 계속 나오지 않게(발소리는 걸을 때마다 난다) 일정 시간 안에는 다시 보여 주지 않는다. 간격은 임시값
        readonly Dictionary<string, float> lastShown = new();
        bool Throttle(string key, float seconds)
        {
            if (lastShown.TryGetValue(key, out var t) && Time.time - t < seconds) return false;
            lastShown[key] = Time.time; return true;
        }

        // 소리 이름 → 안내 문구용 표현
        static string Cause(string src) => src == "던진 유물" ? "던진 유물 소리" : src;

        void CheckTrapHit()
        {
            var tr = traps.HitBy(player, relics.Invuln);
            if (tr == null) return;
            var res = relics.TakeHit();
            run.OnTrapHit(res, tr);                           // 막아 줄 봉인이 없으면 사망(결과 화면)
            if (res == HitResult.Shield) Say("봉인의 조각이 함정을 막고 깨졌습니다");
        }

        void OnClick(double mx, double my)
        {
            switch (relics.ClickAt(player, mx, my))
            {
                case ClickResult.Far: Say("너무 멀어요 — 가까이 가서 눌러요"); break;
                case ClickResult.Escape: run.Escape(); break;
            }
        }

        void OnSlot(int n, bool inside, double mx, double my)
        {
            var o = relics.ThrowSlot(n, player, inside ? mx : (double?)null, inside ? my : (double?)null, Time.timeAsDouble);
            switch (o.Result)
            {
                case ThrowResult.Confirm: Say($"{o.Relic.Def.Name}은(는) 방어를 잃어요 — {RelicSystem.ThrowConfirm:0}초 안에 [{n}]를 한 번 더 누르면 던집니다"); break;
                case ThrowResult.None: Say(o.Relic != null ? $"[{n}] {o.Relic.Def.Name}: 지금 들고 있지 않아요" : $"[{n}] 해당하는 유물 칸이 없어요"); break;
                case ThrowResult.Blocked: Say("그 방향으로는 던질 수 없어요"); break;
            }
        }

        void Say(string m, float seconds = 2.2f) { toast = m; toastUntil = Time.time + seconds; }

        void ShowEvents()
        {
            foreach (var e in torch.Events)
            {
                switch (e.Type)
                {
                    case TorchEventType.Lighting: Sfx.Play(audioSrc, "clack", 0.6f); break;
                    case TorchEventType.LightOn: Sfx.Play(audioSrc, "fire", 0.5f); break;
                    case TorchEventType.TorchOut: Sfx.Play(audioSrc, "out"); break;
                    case TorchEventType.OilPicked: Sfx.Play(audioSrc, "pickup"); break;
                }
                string m = e.Type switch
                {
                    TorchEventType.Lighting => "횃불을 켜는 중… (소리가 납니다)",
                    TorchEventType.LightOn => "횃불이 켜졌습니다",
                    TorchEventType.LightOff => "횃불을 껐습니다",
                    TorchEventType.TorchOut => "기름이 떨어져 횃불이 꺼졌습니다",
                    TorchEventType.NoFuel => "기름이 없습니다",
                    TorchEventType.Exposed => "8초 넘게 켜 둬서 불빛이 멀리서 보입니다",
                    TorchEventType.OilPicked => $"기름병 +{e.Gain}초",
                    _ => null,
                };
                if (m != null) Say(m);
            }
            torch.Events.Clear();              // 수호자가 이미 읽은 뒤
            foreach (var e in relics.Events)
            {
                if (e.Type == RelicEventType.Bell) OnBell(e);
                switch (e.Type)
                {
                    case RelicEventType.Pickup:
                        Sfx.Play(audioSrc, "pickup");
                        if (run.CollapseActive) { Sfx.Play(audioSrc, "rock", collapseSfx ? 0.8f : 1f); collapseSfx = true; }   // v2b: 붕괴 시작 1, 가속 0.8
                        break;
                    case RelicEventType.Regain: Sfx.Play(audioSrc, "pickup"); break;
                    case RelicEventType.LevelUp: Sfx.Play(audioSrc, "danger"); break;
                    case RelicEventType.Throw: Sfx.Play(audioSrc, "clack", 0.6f); break;
                    case RelicEventType.ShardBreak: Sfx.Play(audioSrc, "shard"); break;
                }
                string m = e.Type switch
                {
                    RelicEventType.Pickup => $"획득: {e.Relic.Def.Name} (+{e.Relic.Def.Value}, 잠정 {relics.CarriedScore()}점)",
                    RelicEventType.Regain => $"회수: {e.Relic.Def.Name} (잠정 {relics.CarriedScore()}점)",
                    RelicEventType.Throw => $"던짐: {e.Relic.Def.Name} (잠정 {relics.CarriedScore()}점)",
                    RelicEventType.ShardBreak => "봉인의 조각이 깨졌습니다",
                    _ => null,
                };
                if (m != null) Say(m);
            }
            relics.Events.Clear();             // 수호자·RunState가 이미 읽은 뒤
            foreach (var e in guards.Events)
            {
                double gd = Dist(e.X, e.Y);
                switch (e.Type)
                {
                    case GuardEventType.Wake: Sfx.Play(audioSrc, "growl", gd <= 12 ? 0.9f : 0.5f); break;
                    case GuardEventType.HeardNoise:
                    case GuardEventType.SawLight:
                        if (gd <= 12) Sfx.Play(audioSrc, "growl", Mathf.Max(0.3f, 1f - (float)gd / 12f)); break;
                    case GuardEventType.Chase: Sfx.Play(audioSrc, "growl"); break;
                    case GuardEventType.Reach:
                    case GuardEventType.Caught: Sfx.Play(audioSrc, "growl", 0.8f); break;
                    case GuardEventType.TrapStun: Sfx.Play(audioSrc, "zap", Mathf.Max(0.3f, 1f - (float)gd / 14f)); break;
                    case GuardEventType.Dodged: Sfx.Play(audioSrc, "clack", 0.7f); break;
                    case GuardEventType.ShieldStun: Sfx.Play(audioSrc, "clack", Mathf.Max(0.35f, 1f - (float)gd / 14f)); break;
                }
                string m = e.Type switch
                {
                    GuardEventType.Wake => "수호자가 깨어났습니다",
                    GuardEventType.HeardNoise => $"수호자가 {Cause(e.Source)}에 반응해 경계합니다",
                    GuardEventType.Retarget => Throttle("retarget:" + e.Source, 3f) ? $"수호자의 목표가 {Cause(e.Source)} 쪽으로 바뀌었습니다" : null,
                    GuardEventType.SawLight => "수호자가 횃불 불빛을 봤습니다 — 횃불을 끄고 그 자리를 벗어나세요",
                    GuardEventType.Chase => "수호자가 추격을 시작했습니다",
                    GuardEventType.TrapStun => "수호자가 함정에 걸려 기절했습니다",
                    GuardEventType.Reach => "수호자가 손을 뻗었습니다 — 빨리 벗어나세요",
                    GuardEventType.Dodged => "수호자의 손을 피했습니다",
                    GuardEventType.ShieldStun => "봉인의 조각이 수호자를 막고 깨졌습니다",
                    _ => null,
                };
                if (m != null) Say(m);
            }
            guards.Events.Clear();
            foreach (var e in traps.Events)                    // 함정 경고·작동 소리: 7칸 안에서만 들리고, 작동은 5칸 안(v2b)
            {
                double d = Dist(e.C + 0.5, e.R + 0.5);
                if (d > 7) continue;
                float v = Mathf.Pow(1f - (float)d / 7f, 1.3f);
                if (e.Type == TrapEventType.Warn) Sfx.Play(audioSrc, "warn", v);
                else if (d <= 5) Sfx.Play(audioSrc, "fire", v * 0.8f);
            }
            traps.Events.Clear();
        }

        double Dist(double x, double y) => System.Math.Sqrt((x - player.X) * (x - player.X) + (y - player.Y) * (y - player.Y));

        // 이벤트가 없어도 계속 나는 소리: 수호자 발소리, 가까울 때 심장 소리, 붕괴 진행 중 울림(v2b 메인 루프와 같은 값)
        void AmbientSfx(double dt)
        {
            double nearest = double.MaxValue;
            foreach (var g in guards.Guards)
            {
                if (g.State == GuardState.Dormant || g.State == GuardState.Stunned) continue;
                double d = Dist(g.X, g.Y);
                nearest = System.Math.Min(nearest, d);
                if (g.Moving && (g.State == GuardState.Patrol || g.State == GuardState.Investigate || g.State == GuardState.Alert))
                {
                    guardStepT -= (float)dt;
                    if (guardStepT <= 0f)
                    {
                        if (d <= 9) Sfx.Play(audioSrc, "step", Mathf.Pow(1f - (float)d / 9f, 1.2f));
                        guardStepT = g.State == GuardState.Investigate ? 0.32f : 0.5f;
                    }
                }
            }
            if (nearest <= 6.5)                                // dreadRange
            {
                float k = 1f - (float)nearest / 6.5f;
                heartT -= (float)dt;
                if (heartT <= 0f) { Sfx.Play(audioSrc, "heart", 0.4f + 0.6f * k); heartT = 0.9f - 0.5f * k; }
            }
            if (run.CollapseActive && run.CollapseTotal > 0)
            {
                float k = Mathf.Clamp01(1f - (float)(run.CollapseRemaining / run.CollapseTotal));
                rumbleT -= (float)dt;
                if (rumbleT <= 0f) { Sfx.Play(audioSrc, "rumble", 0.3f + 0.7f * k); rumbleT = 9f - 4f * k; }
            }
        }

        void Paint()
        {
            for (int r = 0; r < map.Rows; r++)
                for (int c = 0; c < map.Cols; c++)
                {
                    float v = (float)vis.Visible[r, c];
                    float k = v > 0 ? v : (vis.Seen[r, c] ? 0.22f : 0f);       // 본 적 있는 칸은 어렴풋이 기억
                    // 아트가 있으면 그림 색을 그대로 두고 밝기만 곱한다(빛 안은 따뜻하게, 기억은 차갑게). 없으면 예전 색 사각형.
                    var baseCol = art ? (v > 0 ? new Color(1f, 0.93f, 0.8f) : new Color(0.82f, 0.9f, 1f))
                                      : (map.IsWall(c, r) ? new Color(0.28f, 0.32f, 0.34f) : new Color(0.45f, 0.38f, 0.30f));
                    tiles[r, c].color = new Color(baseCol.r * k, baseCol.g * k, baseCol.b * k, 1f);
                }
            foreach (var it in items)
            {
                bool lit = vis.Visible[it.r, it.c] > 0;
                bool show = it.kind switch
                {
                    "oil" => !torch.Oils[it.idx].Taken,               // 기름병은 어둠 속에서도 불씨처럼 보임
                    _ => vis.Seen[it.r, it.c],
                };
                it.sr.enabled = show;
                float a = (it.kind == "oil" && !lit) ? 0.5f : 1f;
                if (it.kind == "start" && art) a = Mathf.Clamp01(0.85f + 0.25f * Mathf.Sin(Time.time * 3f));     // v2b drawEntrance 맥동
                var col = it.col;
                if (it.kind == "trap")
                {
                    // 빛 안이거나 1.5칸 안이면 상태가 보이고, 아니면 대기 모습(작동 상태 숨김). v2b drawTrap
                    var tr = traps.Traps[it.idx];
                    double d = System.Math.Sqrt(System.Math.Pow(tr.C + 0.5 - player.X, 2) + System.Math.Pow(tr.R + 0.5 - player.Y, 2));
                    bool vis2 = lit || d <= TrapSystem.WarnGlowRadius;
                    var ph = vis2 ? tr.Phase : TrapPhase.Idle;
                    if (it.col == Color.white)                 // 아트: 상태별 스프라이트로 교체
                    {
                        var ts = Spr(ph switch { TrapPhase.Warn => "Traps/trap_warn", TrapPhase.Active => "Traps/trap_active", TrapPhase.Rest => "Traps/trap_rest", _ => "Traps/trap_idle" });
                        if (ts != null) it.sr.sprite = ts;
                    }
                    else col = ph switch
                    {
                        TrapPhase.Warn => new Color(0.69f, 0.16f, 0.14f),
                        TrapPhase.Active => new Color(1f, 0.29f, 0.24f),
                        TrapPhase.Rest => new Color(0.16f, 0.11f, 0.11f),
                        _ => new Color(0.24f, 0.33f, 0.34f),
                    };
                }
                it.sr.color = new Color(col.r, col.g, col.b, a);
            }
            PaintPlayer();
            PaintGuard();
            foreach (var kv in relicViews)
            {
                var rl = kv.Key; var (body, spark) = kv.Value;
                var (x, y) = GameMap.CellCenter(rl.R, rl.C);
                float bob = body.sprite != white ? Mathf.Round(Mathf.Sin(Time.time * 2.5f + rl.C) * 1.5f) * 2f / 36f : 0f;   // v2b: 위아래로 살짝 뜬다(3px)
                body.transform.position = new Vector3(x, y + bob, 0f);
                spark.transform.position = new Vector3(x + 0.08f, y + 0.11f + bob, 0f);
                // 본 유물만 그린다(어둠 속에서도 기억). 횃불 빛 밖이면 어둡게. 던진 유물은 주황.
                body.enabled = !rl.Taken && rl.Seen;
                bool inLight = torch.Light && vis.Visible[rl.R, rl.C] > 0;
                bool relicArt = body.sprite != white;
                var col = relicArt ? (rl.Thrown ? new Color(1f, 0.78f, 0.5f) : Color.white)           // 아트: 던진 유물만 주황빛
                                   : (rl.Thrown ? new Color(1f, 0.6f, 0.24f) : new Color(0.3f, 0.9f, 0.9f));
                body.color = new Color(col.r, col.g, col.b, inLight ? 1f : 0.55f);
                // 횃불 밖 5칸 안의 유물은 작게 깜빡인다(어둠 속 단서)
                bool blink = Mathf.FloorToInt(Time.time * 2f + rl.C * 0.7f + rl.R) % 4 == 0;
                bool sp = relics.Sparkle(rl, player, torch.Light) && blink;
                spark.enabled = sp;
                if (relics.Sparkle(rl, player, torch.Light) && !hintedSpark)
                {
                    hintedSpark = true;
                    Say("어둠 속에서 무언가 반짝인다 — 가까이 가서 F로 비춰 보자. 한 번 보면 불을 꺼도 기억에 남아 주울 수 있다");
                    toastUntil = Time.time + 4.2f;
                }
            }
        }

        // 플레이어: 마지막으로 이동한 방향을 보고, 걸을 때 발을 번갈아 딛는다. 봉인의 조각을 들면 청록 고리, 횃불이 켜지면 손 앞에 불꽃. (v2b drawPlayer·lanternPos)
        void PaintPlayer()
        {
            bool dead = run.Phase == RunPhase.Dead;
            bool blink = relics.Invuln > 0 && Mathf.FloorToInt(Time.time * 14f) % 2 == 0;       // 무적 중 깜빡임
            bool show = !dead && !blink;
            double aim = player.Dir, ca = System.Math.Cos(aim), sa = System.Math.Sin(aim);
            string view = System.Math.Abs(ca) >= System.Math.Abs(sa) ? (ca < 0 ? "left" : "right") : (sa > 0 ? "down" : "up");
            int f = moved ? WalkCycle[Mathf.FloorToInt(Time.time * 8f) % WalkCycle.Length] : 0;
            var pos = new Vector3((float)player.X, -(float)player.Y, 0f);
            playerSr.transform.position = pos; playerSr.enabled = show;
            if (playerSr.sprite != white) { var ps = Spr($"Characters/warrior_hood_{view}_{f}"); if (ps != null) playerSr.sprite = ps; }
            shadowP.enabled = show; shadowP.transform.position = new Vector3(pos.x, pos.y - 8f / 36f, 0f);
            aura.enabled = show && relics.Shard == ShardState.Held;
            aura.transform.position = pos;
            aura.color = new Color(0.31f, 0.82f, 0.77f, 0.18f + 0.14f * Mathf.Sin(Time.time * 4f) + 0.15f);
            bool lit = torch.Light, lighting = torch.Lighting > 0;
            flame.enabled = !dead && (lit || lighting);
            if (flame.sprite != white)
            {
                // 횃불 손잡이 맨 위 픽셀(30x48 PNG, 왼쪽 위가 원점)을 방향별로 읽어 둔 값. 피벗은 (0.5, 1/3)이라 몸 기준점은 (15, 32)px.
                // 불꽃(10x12, 중심 피벗)은 아래쪽 2px을 손잡이에 걸쳐 놓는다. 걷기 프레임마다 손이 1~2px 움직이는 것은 따라가지 않는다(고정).
                var hp = view switch { "down" => new Vector2(5f, 23f), "up" => new Vector2(23f, 25f), "left" => new Vector2(5f, 27f), _ => new Vector2(23f, 22f) };
                flame.transform.position = new Vector3(pos.x + (hp.x - 15f) / 36f, pos.y + (32f - hp.y) / 36f + 4f / 36f, 0f);
            }
            else flame.transform.position = new Vector3((float)(player.X + ca * PlayerMover.Radius * 0.9), -(float)(player.Y + sa * PlayerMover.Radius * 0.9) + 4f / 36f, 0f);
            float fl = flameBase * (1f + 0.12f * Mathf.Sin(Time.time * 18f));
            flame.transform.localScale = new Vector3(fl, fl, 1f);
            flame.color = new Color(1f, 1f, 1f, lit ? 1f : 0.5f);
        }

        // 수호자 표시(임시 색 사각형). v2b 규칙: 빛 안이면 보이고, 빛 밖이면 가까울 때(4칸) 희미한 실루엣, 어둠 속에서는 붉은 눈만 (쫓아올 때는 벽 너머라도 더 크게)
        void PaintGuard()
        {
            var gd = guards.Guards[0];
            var pos = new Vector3((float)gd.X, -(float)gd.Y, 0f);
            bool lit = vis.Visible[(int)System.Math.Floor(gd.Y), (int)System.Math.Floor(gd.X)] > 0;
            double d = System.Math.Sqrt(System.Math.Pow(gd.X - player.X, 2) + System.Math.Pow(gd.Y - player.Y, 2));
            bool awakeNotStun = gd.State != GuardState.Dormant && gd.State != GuardState.Stunned;
            Color col = gd.State switch
            {
                GuardState.Dormant => new Color(0.55f, 0.55f, 0.6f),
                GuardState.Patrol => new Color(0.75f, 0.2f, 0.2f),
                GuardState.Alert => new Color(0.95f, 0.55f, 0.15f),
                GuardState.Investigate => new Color(1f, 0.1f, 0.1f),
                _ => new Color(0.5f, 0.45f, 0.3f),
            };
            if (guardArt)
            {
                var gs = Spr(gd.State switch { GuardState.Dormant => "Characters/guardian_dormant", GuardState.Patrol => "Characters/guardian_patrol", GuardState.Alert => "Characters/guardian_alert", GuardState.Investigate => "Characters/guardian_investigate", _ => "Characters/guardian_stunned" });
                if (gs != null) guardBody.sprite = gs;
                col = Color.white;                                            // 상태 색은 스프라이트가 이미 가지고 있다
                guardBody.flipX = System.Math.Cos(gd.Dir) < -0.3;
                if (awakeNotStun && gd.Moving && Mathf.FloorToInt(Time.time * 7f) % 2 == 1) pos.y += 3f / 36f;     // v2b: 걸을 때 3px 들썩
            }
            float a = 0f;
            if (lit) a = 1f;
            else if (awakeNotStun && d <= GuardSystem.NearRange) a = 0.35f + 0.45f * (1f - (float)d / (float)GuardSystem.NearRange);
            guardBody.enabled = a > 0f; guardBody.transform.position = pos; guardBody.color = new Color(col.r, col.g, col.b, a);
            shadowG.enabled = guardBody.enabled; shadowG.transform.position = new Vector3((float)gd.X, -(float)gd.Y - 10f / 36f, 0f);
            bool stun = gd.State == GuardState.Stunned && guardBody.enabled;                  // 기절하면 별 두 개가 머리 위를 돈다
            for (int i = 0; i < stars.Length; i++)
            {
                stars[i].enabled = stun;
                if (!stun) continue;
                float ang = Time.time * 5f + i * Mathf.PI;
                stars[i].transform.position = new Vector3((float)gd.X + Mathf.Cos(ang) * 12f / 36f, -(float)gd.Y + 40f / 36f - Mathf.Sin(ang) * 4f / 36f, 0f);
            }

            bool eyes = false, chasing = gd.State == GuardState.Investigate || gd.State == GuardState.Alert;
            if (!lit && awakeNotStun)
            {
                if (chasing) eyes = d <= GuardSystem.EyeRange * 1.6;
                else eyes = d <= GuardSystem.EyeRange && Los.Has(map, player.X, player.Y, gd.X, gd.Y) && Mathf.FloorToInt(Time.time * 2f + (float)gd.X) % 6 != 0;
            }
            eyeL.enabled = eyeR.enabled = eyes;
            if (eyes)
            {
                if (guardArt)
                {
                    // 스프라이트의 눈(54x54 PNG): 왼눈 x15~20, 오른눈 x33~38, y21~26 (각 6x6px). 피벗(27, 36)px 기준으로 같은 자리에 겹쳐 그린다.
                    float es = (chasing ? 8f : 6f) / 36f;
                    eyeL.transform.localScale = eyeR.transform.localScale = new Vector3(es, es, 1f);
                    eyeL.transform.position = pos + new Vector3(-9f / 36f, 12f / 36f, 0f); eyeR.transform.position = pos + new Vector3(9f / 36f, 12f / 36f, 0f);
                    var ec = gd.State == GuardState.Alert ? new Color(1f, 0.70f, 0.28f) : (gd.State == GuardState.Investigate ? new Color(1f, 0.10f, 0.10f) : new Color(1f, 0.29f, 0.24f));
                    eyeL.color = eyeR.color = ec;                                  // 스프라이트 눈 색과 같게(순찰 빨강, 경계 주황, 추적 진한 빨강)
                }
                else
                {
                    float s = chasing ? 0.14f : 0.1f;
                    eyeL.transform.localScale = eyeR.transform.localScale = new Vector3(s, s, 1f);
                    eyeL.transform.position = pos + new Vector3(-0.12f, 0.2f, 0f); eyeR.transform.position = pos + new Vector3(0.12f, 0.2f, 0f);
                }
                if (!hintedEyes) { hintedEyes = true; Say("어둠 속에서 눈이 빛난다… 횃불을 끄고 조용히 비켜서면 지나간다"); toastUntil = Time.time + 3.4f; }
            }
            // 손을 뻗는 중: 붉은 원이 커진다 — 이 안에서 벗어나야 산다
            grabRing.enabled = gd.Grab != null;
            if (gd.Grab != null)
            {
                float k = 1f - (float)gd.Grab.Value / (float)GuardSystem.GrabTime, r = (float)GuardSystem.GrabRange * (0.5f + 0.5f * k);
                grabRing.sprite = ringSprite; grabRing.transform.position = pos; grabRing.transform.localScale = new Vector3(r * 2f, r * 2f, 1f);
                grabRing.color = new Color(1f, 0.29f, 0.24f, 0.5f + 0.5f * k);
            }
        }

        // HUD(임시 OnGUI): 줄마다 16x16 도트 아이콘(Assets/Art/Sprites/Icons)을 2배로 그리고 옆에 글자를 둔다.
        void Icon(float x, float y, string key, float size = 20f)
        {
            var sp = Spr(key); if (sp == null) return;
            var t = sp.texture; var r = sp.textureRect;
            GUI.DrawTextureWithTexCoords(new Rect(x, y, size, size), t, new Rect(r.x / t.width, r.y / t.height, r.width / t.width, r.height / t.height));
        }

        void Row(ref float y, string icon, string text, Color? tint = null)
        {
            Icon(10, y, icon);
            var prev = GUI.color; if (tint != null) GUI.color = tint.Value;
            GUI.Label(new Rect(36, y, 1000, 22), text);
            GUI.color = prev; y += 24f;
        }

        void OnGUI()
        {
            if (torch == null) return;
            string state = torch.Light ? "켜짐" : (torch.Lighting > 0 ? "켜는 중" : "꺼짐");
            float y = 8f;
            Row(ref y, "Icons/torch", $"횃불 {state}   기름 {Mathf.CeilToInt((float)torch.Fuel)}초",
                torch.Fuel <= 0 ? new Color(1f, 0.4f, 0.35f) : (Color?)null);
            if (Time.time < toastUntil && run.Phase == RunPhase.Play) GUI.Label(new Rect(10, y, 1100, 24), toast);
            y += 26f;
            var list = relics.SlotList();
            for (int i = 0; i < list.Count; i++)
            {
                var rl = list[i];
                string st = relics.Carried.Contains(rl) ? "들고 있음" : (rl.Thrown ? "바닥(던짐)" : (rl.Seen ? "본 유물" : "아직 못 봄"));
                Row(ref y, "Icons/" + rl.Def.Id, $"[{i + 1}] {rl.Def.Name} +{rl.Def.Value} · {st}");
            }
            Row(ref y, "Icons/bag", $"소지 {relics.Carried.Count}개  잠정 {relics.CarriedScore()}점 × {relics.GreedMult():0.##} = {relics.EscapeScore()}점");
            if (run.CollapseActive)
                Row(ref y, "Icons/hourglass", $"붕괴까지 {Mathf.CeilToInt((float)run.CollapseRemaining)}초   경과 {RunState.FmtTime(run.Time).Substring(0, 5)}",
                    run.CollapseRemaining <= 30 ? new Color(1f, 0.4f, 0.35f) : (Color?)null);
            else Row(ref y, "Icons/hourglass", "붕괴: 첫 유물을 줍는 순간 시작");
            GUI.Label(new Rect(10, Screen.height - 28, 1100, 24), "[F] 횃불  [WASD] 이동  [좌클릭] 줍기·탈출  [우클릭] 던지기  [1·2·3] 슬롯 던지기  [M] 소리");
            if (run.Phase != RunPhase.Play) DrawResult();
        }

        // 결과 화면(임시 OnGUI). v2b resultScreen 항목: 점수·등급·최고 기록·가져온/잃은 유물·시간·사망 원인·로그 복사
        void DrawResult()
        {
            bool ok = run.Phase == RunPhase.Escaped;
            // 로그 칸·복사 버튼은 제작자용입니다. 에디터와 Development Build에서만 보이고, 배포(WebGL) 빌드에서는 나오지 않습니다.
            bool devLog = Application.isEditor || Debug.isDebugBuild;
            float w = Mathf.Min(780f, Screen.width - 40f), h = Mathf.Min(devLog ? 620f : 400f, Screen.height - 40f);
            var area = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.Box(area, GUIContent.none); GUI.Box(area, GUIContent.none);          // 두 번 그려 배경을 더 어둡게
            GUILayout.BeginArea(new Rect(area.x + 16, area.y + 12, w - 32, h - 24));
            var big = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold };
            big.normal.textColor = ok ? new Color(0.5f, 0.95f, 0.6f) : new Color(1f, 0.4f, 0.35f);
            GUILayout.Label(ok ? "탈출 성공" : "사망 — 유물을 모두 잃었습니다", big);
            GUILayout.Label($"확보 점수  {run.FinalScore}점{(newBest && run.FinalScore > 0 ? "  — 신기록!" : "")}   (목표 {RunState.MaxScoreRound1}점)");
            GUILayout.Label($"등급  {RunState.RankFor(run.FinalScore).Name}      라운드 1      최고 기록 {bestScore}점");
            GUILayout.Label("가져온 유물  " + (ok && relics.Carried.Count > 0 ? string.Join(", ", relics.Carried.ConvertAll(r => $"{r.Def.Name}({r.Def.Value})")) : "없음"));
            GUILayout.Label("잃거나 소비한 유물  " + (relics.Lost.Count > 0 ? string.Join(", ", relics.Lost.ConvertAll(l => $"{l.name} — {l.reason}")) : "없음"));
            GUILayout.Label("탐험 시간  " + RunState.FmtTime(run.Time));
            if (!ok) GUILayout.Label("사망 원인  " + run.DeathCause);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("같은 맵으로 다시 (R)", GUILayout.Height(30))) Restart(seed);
            if (GUILayout.Button("새 맵으로 (N)", GUILayout.Height(30))) Restart((uint)Random.Range(1, 1000000));
            if (devLog && GUILayout.Button("로그 복사", GUILayout.Height(30)))
            {
                GUIUtility.systemCopyBuffer = FullLog(); copyMsg = "로그를 복사했습니다 (안 되면 아래 상자에서 직접 선택해 복사)";
            }
            GUILayout.EndHorizontal();
            if (devLog)
            {
                GUILayout.Label(copyMsg);
                logScroll = GUILayout.BeginScrollView(logScroll, GUILayout.Height(h - 300f));
                GUILayout.TextArea(FullLog());
                GUILayout.EndScrollView();
            }
            GUILayout.EndArea();
        }

        string FullLog() => run.LogText() + "\n\n[세션 요약]\n" + string.Join("\n", sessionRuns);

        Sprite Spr(string key) => lib != null ? lib.Get(key) : null;

        /// 아트 스프라이트가 있으면 그것(스케일 1 = 원본 크기, 색은 흰색), 없으면 색 사각형으로 만든다.
        GameObject Make(string artKey, int c, int r, float fallbackSize, Color fallbackColor, int order)
        {
            var s = art && artKey != null ? Spr(artKey) : null;
            return s != null ? Quad(s, c, r, 1f, Color.white, order) : Quad(white, c, r, fallbackSize, fallbackColor, order);
        }

        void AddItem(string artKey, int r, int c, float fallbackSize, Color fallbackColor, int order, string kind, int idx)
        {
            var sr = Make(artKey, c, r, fallbackSize, fallbackColor, order).GetComponent<SpriteRenderer>();
            items.Add((sr, r, c, sr.sprite == white ? fallbackColor : Color.white, kind, idx));
        }

        void Sync()
        {
            var (x, y) = (player.X, player.Y);
            // 도트가 번지지 않게: 화면 높이에 맞춰 정수 배율(1 도트 = k 화면 픽셀)로 카메라 크기를 정하고, 카메라 위치를 그 픽셀 격자에 맞춘다.
            // 세로 약 12칸(CameraSize 6)에 가장 가까운 배율. Unity의 Pixel Perfect Camera 패키지 없이 같은 효과를 낸다(임시).
            float h = Mathf.Max(1, Screen.height);
            int k = Mathf.Max(1, Mathf.RoundToInt(h / (2f * CameraSize * 36f)));
            cam.orthographicSize = h / (2f * 36f * k);
            float step = 1f / (36f * k);
            cam.transform.position = new Vector3(Mathf.Round((float)x / step) * step, Mathf.Round(-(float)y / step) * step, -10f);   // Unity: (x, -y)
        }

        // 칸 (r,c) 중심 = (c+0.5, -(r+0.5))
        GameObject Quad(Sprite s, int c, int r, float size, Color col, int order)
        {
            var go = new GameObject($"q_{r}_{c}"); go.transform.SetParent(root, false);
            var (x, y) = GameMap.CellCenter(r, c);
            go.transform.position = new Vector3(x, y, 0f);
            go.transform.localScale = new Vector3(size, size, 1f);
            var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = s; sr.color = col; sr.sortingOrder = order;
            return go;
        }

        static Sprite MakeWhiteSprite()
        {
            var tex = new Texture2D(1, 1) { filterMode = FilterMode.Point };
            tex.SetPixel(0, 0, Color.white); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);   // PPU 1 = 1유닛
        }
    }
}
