using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Duckov;
using Duckov.Buffs; 
using ItemStatsSystem;
using ItemStatsSystem.Stats;

namespace PileErico
{
    public class Enhancer_Ultraman : BossEnhancer
    {
        public override string TargetID => "EnemyPreset_Melee_UltraMan";

        // 定义颜色
        private static readonly Color RageColor = new Color(1.0f, 0.1f, 0.1f); // 爆发红
        private static readonly Color PaleGoldColor = new Color(1.0f, 0.92f, 0.75f); // 常态淡金

        // 运行时状态
        private bool _isPhaseTwo = false;
        private Coroutine _currentBehavior = null!; 
        private CharacterMainControl _bossRef = null!;
        
        // 未命中计数器
        private int _missCount = 0;

        // ===================================================================
        // 1. 强化入口
        // ===================================================================
        public override void OnEnhance(CharacterMainControl boss)
        {
            if (boss == null) return;
            _bossRef = boss;

            Log($"[Ultraman] 开始强化 ID: {TargetID}");

            // --- 基础属性 ---
            ApplyStat(boss, "MaxHealth", 5.0f, isMultiplier: true); 
            boss.transform.localScale *= 2.0f; 

            // 降低 Boss 基础移速
            ApplyStat(boss, "WalkSpeed", 0.65f, isMultiplier: true); 
            ApplyStat(boss, "RunSpeed", 0.65f, isMultiplier: true); 

            // --- 注册二阶段监听 ---
            BossPhaseController phaseController = boss.gameObject.AddComponent<BossPhaseController>();
            phaseController.Setup(boss, 0.5f, OnPhaseTwoTrigger);

            // --- 启动 P1 循环 ---
            _currentBehavior = _bossRef.StartCoroutine(PhaseOneLoop(boss));
        }

        // ===================================================================
        // 2. 阶段切换逻辑
        // ===================================================================
        private void OnPhaseTwoTrigger(CharacterMainControl boss)
        {
            if (_isPhaseTwo) return; 
            _isPhaseTwo = true;

            // [修改] 二阶段常态：变为淡金色 (发光)
            ChangeModelColor(boss, PaleGoldColor, true, 4.0f);
            
            // 处理光源
            var light = boss.GetComponentInChildren<Light>();
            if (light != null) { light.color = PaleGoldColor; light.intensity = 5.0f; }
            else AddPointLight(boss, PaleGoldColor, 15f, 5.0f);

            // 2. 强制打断
            if (_currentBehavior != null) _bossRef.StopCoroutine(_currentBehavior);
            boss.SetMoveInput(Vector3.zero); 
            
            // 3. 启动 P2 循环
            _currentBehavior = _bossRef.StartCoroutine(PhaseTwoLoop(boss));
        }

        // ===================================================================
        // 3. 行为循环
        // ===================================================================
        
        private IEnumerator PhaseOneLoop(CharacterMainControl boss)
        {
            float cooldown = 8.0f;

            while (!_bossRef.Health.IsDead && !_isPhaseTwo)
            {
                yield return new WaitForSeconds(0.5f);

                if (CharacterMainControl.Main == null) continue;
                Transform target = CharacterMainControl.Main.transform;
                float dist = Vector3.Distance(boss.transform.position, target.position);

                // Miss 计数判定
                if (_missCount >= 3)
                {
                    yield return Skill_JumpSmash(boss, target, isInstant: true);
                }
                else
                {
                    if (dist <= 5.0f) yield return Skill_WarCry(boss, target);
                    else if (dist <= 10.0f) yield return Skill_Charge_P1(boss, target);
                    else yield return Skill_JumpSmash(boss, target, isInstant: false);
                }

                yield return new WaitForSeconds(cooldown);
            }
        }

        private IEnumerator PhaseTwoLoop(CharacterMainControl boss)
        {
            if (CharacterMainControl.Main != null)
                yield return Skill_Charge_P2_Triple(boss, CharacterMainControl.Main.transform);

            float cooldown = 6.0f;

            while (!_bossRef.Health.IsDead)
            {
                yield return new WaitForSeconds(0.2f); 

                if (CharacterMainControl.Main == null) continue;
                Transform target = CharacterMainControl.Main.transform;
                float dist = Vector3.Distance(boss.transform.position, target.position);

                if (_missCount >= 3)
                {
                    yield return Skill_JumpSmash(boss, target, isInstant: true);
                }
                else
                {
                    if (dist <= 5.0f) yield return Skill_WarCry(boss, target);
                    else if (dist <= 10.0f) yield return Skill_Charge_P2_Triple(boss, target);
                    else yield return Skill_JumpSmash(boss, target, isInstant: false);
                }

                yield return new WaitForSeconds(cooldown);
            }
        }

        // ===================================================================
        // 4. 技能具体实现
        // ===================================================================

        private IEnumerator Skill_Charge_P1(CharacterMainControl boss, Transform target)
        {
            boss.PopText("！");
            yield return Helper_Windup(boss, target, 2.0f, null); 
            yield return Helper_ChargeMove(boss, 15.0f, 0.67f);
            yield return Helper_LockFoot(boss, 1.0f);
        }

        private IEnumerator Skill_Charge_P2_Triple(CharacterMainControl boss, Transform target)
        {
            float duration = 10.0f / 15.0f; 

            boss.PopText("！");
            yield return Helper_Windup(boss, target, 1.0f, null);
            yield return Helper_ChargeMove(boss, 15.0f, duration); 
            yield return Helper_LockFoot(boss, 0.5f); 

            yield return Helper_Windup(boss, target, 0.5f, null);
            yield return Helper_ChargeMove(boss, 15.0f, duration);
            yield return Helper_LockFoot(boss, 0.5f);

            yield return Helper_Windup(boss, target, 0.5f, null);
            yield return Helper_ChargeMove(boss, 15.0f, duration);
            yield return Helper_LockFoot(boss, 1.0f); 
        }

        private IEnumerator Skill_JumpSmash(CharacterMainControl boss, Transform target, bool isInstant = false)
        {
            if (!isInstant)
            {
                yield return Helper_Windup(boss, target, 1.0f, "别想逃！");
            }
            else 
            {
                boss.PopText("！"); 
                boss.SetMoveInput(Vector3.zero);
                yield return null; 
            }

            Vector3 startPos = boss.transform.position;
            Vector3 landPos = target.position; 
            float timer = 0f;
            float duration = 0.8f; 

            // 静态指示器
            boss.StartCoroutine(AnimateStaticRangeIndicator(landPos, 3.0f, duration));

            while (timer < duration)
            {
                if (boss.Health.IsDead) yield break;
                float t = timer / duration;
                Vector3 currentPos = Vector3.Lerp(startPos, landPos, t);
                currentPos.y += Mathf.Sin(t * Mathf.PI) * 4.0f; 
                boss.transform.position = currentPos;
                timer += Time.deltaTime;
                yield return null;
            }
            boss.transform.position = landPos; 

            bool hit = false;
            float hitRadius = 3.0f;
            
            if (Vector3.Distance(boss.transform.position, target.position) <= hitRadius)
            {
                var player = target.GetComponent<CharacterMainControl>();
                if (player != null)
                {
                    DamageInfo info = new DamageInfo(boss);
                    info.damageValue = 15f;
                    info.damageType = DamageTypes.realDamage;
                    info.ignoreArmor = true;
                    player.Health.Hurt(info);
                    hit = true;
                }
            }

            if (hit) _missCount = 0; 
            else _missCount++;       

            if (_isPhaseTwo && hit)
            {
                yield return Skill_WarCry(boss, target, isInstant: true);
            }
            else
            {
                yield return Helper_LockFoot(boss, 1.0f);
            }
        }

        private IEnumerator Skill_WarCry(CharacterMainControl boss, Transform target, bool isInstant = false)
        {
            if (!isInstant)
            {
                boss.StartCoroutine(AnimateRangeIndicator(boss, 5.0f, 1.5f));
                yield return Helper_Windup(boss, target, 1.5f, "……");
            }

            boss.PopText("嘎！！！！"); 

            bool hit = false;
            if (target != null)
            {
                var player = target.GetComponent<CharacterMainControl>();
                if (player != null && Vector3.Distance(boss.transform.position, player.transform.position) <= 5.0f)
                {
                    boss.StartCoroutine(DoTDamageRoutine(boss, player));
                    boss.StartCoroutine(ApplyTemporaryStat(player, "WalkSpeed", 0.7f, 4.0f));
                    boss.StartCoroutine(ApplyTemporaryStat(player, "RunSpeed", 0.7f, 4.0f));
                    hit = true;
                }
            }

            if (hit) _missCount = 0;
            else _missCount++;

            if (_isPhaseTwo)
            {
                // 数值强化
                boss.StartCoroutine(ApplyTemporaryStat(boss, "MoveSpeedFactor", 1.3f, 4.0f));
                boss.StartCoroutine(ApplyTemporaryStat(boss, "MeleeDamageMultiplier", 1.2f, 4.0f));
                
                // [新增] 视觉强化：变红持续 4秒，然后变回淡金
                boss.StartCoroutine(ApplyTemporaryVisualBuff(boss, RageColor, 4.0f));
            }

            yield return null; 
        }

        // ===================================================================
        // 5. 通用辅助协程 & 工具
        // ===================================================================

        // [新增] 视觉 Buff 协程
        private IEnumerator ApplyTemporaryVisualBuff(CharacterMainControl boss, Color tempColor, float duration)
        {
            // 变红
            ChangeModelColor(boss, tempColor, true, 4.0f);
            UpdateBossLight(boss, tempColor);

            yield return new WaitForSeconds(duration);

            // 恢复为淡金 (前提是 Boss 还活着且处于 P2)
            if (boss != null && !boss.Health.IsDead && _isPhaseTwo)
            {
                ChangeModelColor(boss, PaleGoldColor, true, 4.0f);
                UpdateBossLight(boss, PaleGoldColor);
            }
        }

        // [新增] 统一更新光源颜色辅助方法
        private void UpdateBossLight(CharacterMainControl boss, Color color)
        {
            var light = boss.GetComponentInChildren<Light>();
            if (light != null) 
            { 
                light.color = color; 
            }
        }

        private IEnumerator AnimateRangeIndicator(CharacterMainControl boss, float maxRadius, float duration)
        {
            GameObject? fillObj = null;
            GameObject? borderObj = null;
            try 
            {
                CreateIndicatorObjects(out fillObj, out borderObj, maxRadius);
            }
            catch (Exception) { yield break; }

            float timer = 0f;
            Vector3 initialScale = new Vector3(0.1f, 0.05f, 0.1f); 
            Vector3 targetScale = new Vector3(maxRadius * 2, 0.05f, maxRadius * 2); 

            while (timer < duration && fillObj != null && borderObj != null && boss != null)
            {
                Vector3 centerPos = boss.transform.position + Vector3.up * 0.1f;
                fillObj.transform.position = centerPos;
                borderObj.transform.position = centerPos;

                float progress = timer / duration;
                fillObj.transform.localScale = Vector3.Lerp(initialScale, targetScale, progress);
                
                timer += Time.deltaTime;
                yield return null;
            }
            if (fillObj != null) GameObject.Destroy(fillObj);
            if (borderObj != null) GameObject.Destroy(borderObj);
        }

        private IEnumerator AnimateStaticRangeIndicator(Vector3 targetPos, float maxRadius, float duration)
        {
            GameObject? fillObj = null;
            GameObject? borderObj = null;
            try 
            {
                CreateIndicatorObjects(out fillObj, out borderObj, maxRadius);
            }
            catch (Exception) { yield break; }

            Vector3 fixedPos = targetPos + Vector3.up * 0.1f;
            if (fillObj != null) fillObj.transform.position = fixedPos;
            if (borderObj != null) borderObj.transform.position = fixedPos;

            float timer = 0f;
            Vector3 initialScale = new Vector3(0.1f, 0.05f, 0.1f); 
            Vector3 targetScale = new Vector3(maxRadius * 2, 0.05f, maxRadius * 2); 

            while (timer < duration && fillObj != null && borderObj != null)
            {
                float progress = timer / duration;
                fillObj.transform.localScale = Vector3.Lerp(initialScale, targetScale, progress);
                
                timer += Time.deltaTime;
                yield return null;
            }
            if (fillObj != null) GameObject.Destroy(fillObj);
            if (borderObj != null) GameObject.Destroy(borderObj);
        }

        private void CreateIndicatorObjects(out GameObject fill, out GameObject border, float radius)
        {
            fill = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            var col = fill.GetComponent<Collider>();
            if (col) GameObject.Destroy(col);
            var r = fill.GetComponent<Renderer>();
            if (r) SetupTransparentMaterial(r, new Color(1f, 0.3f, 0.3f, 0.5f)); 

            border = new GameObject("RangeBorder");
            var line = border.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 60;
            line.startWidth = 0.15f;
            line.endWidth = 0.15f;
            
            Material lineMat = CreateTransparentMaterial(new Color(0.5f, 0f, 0f, 0.5f));
            line.material = lineMat;
            line.startColor = new Color(0.5f, 0f, 0f, 0.5f);
            line.endColor = new Color(0.5f, 0f, 0f, 0.5f);

            float angleStep = 360f / line.positionCount;
            for (int i = 0; i < line.positionCount; i++)
            {
                float angle = i * angleStep * Mathf.Deg2Rad;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;
                line.SetPosition(i, new Vector3(x, 0, z));
            }
        }

        private Material CreateTransparentMaterial(Color color)
        {
            Shader? shader = Shader.Find("Sprites/Default"); 
            if (shader == null) shader = Shader.Find("Standard"); 

            Material mat = new Material(shader);
            mat.color = color;
            mat.renderQueue = 3000; 
            return mat;
        }

        private void SetupTransparentMaterial(Renderer renderer, Color color)
        {
            renderer.material = CreateTransparentMaterial(color);
        }

        private IEnumerator Helper_Windup(CharacterMainControl boss, Transform target, float time, string? text)
        {
            if (!string.IsNullOrEmpty(text)) boss.PopText(text); 
            
            boss.SetMoveInput(Vector3.zero);
            Vector3 lockPos = boss.transform.position;
            float t = 0;
            while(t < time)
            {
                if (boss.Health.IsDead) yield break;
                boss.transform.position = lockPos; 
                if (target != null) boss.transform.LookAt(new Vector3(target.position.x, boss.transform.position.y, target.position.z));
                t += Time.deltaTime;
                yield return null;
            }
        }

        private IEnumerator Helper_ChargeMove(CharacterMainControl boss, float speed, float duration)
        {
            float t = 0;
            bool hasHit = false; 

            while (t < duration)
            {
                if (boss.Health.IsDead) yield break;
                boss.transform.position += boss.transform.forward * speed * Time.deltaTime;

                if (!hasHit && CharacterMainControl.Main != null)
                {
                    float dist = Vector3.Distance(boss.transform.position, CharacterMainControl.Main.transform.position);
                    if (dist <= 2.0f)
                    {
                        var player = CharacterMainControl.Main;
                        DamageInfo info = new DamageInfo(boss);
                        info.damageValue = 15f; 
                        info.damageType = DamageTypes.realDamage;
                        player.Health.Hurt(info);
                        hasHit = true;
                    }
                }
                t += Time.deltaTime;
                yield return null;
            }
        }

        private IEnumerator Helper_LockFoot(CharacterMainControl boss, float time)
        {
            boss.SetMoveInput(Vector3.zero);
            Vector3 lockPos = boss.transform.position;
            float t = 0;
            while (t < time)
            {
                if (boss.Health.IsDead) yield break;
                boss.transform.position = lockPos;
                t += Time.deltaTime;
                yield return null;
            }
        }

        private IEnumerator DoTDamageRoutine(CharacterMainControl boss, CharacterMainControl target)
        {
            float damagePerTick = target.Health.MaxHealth * 0.05f; 
            for (int i = 0; i < 4; i++)
            {
                if (target == null || target.Health.IsDead) yield break;
                DamageInfo info = new DamageInfo(boss);
                info.damageValue = damagePerTick;
                info.damageType = DamageTypes.realDamage;
                info.ignoreArmor = true;
                target.Health.Hurt(info);
                yield return new WaitForSeconds(1.0f);
            }
        }

        private IEnumerator ApplyTemporaryStat(CharacterMainControl target, string statName, float multiplier, float duration)
        {
            if (target == null || target.CharacterItem == null) yield break;
            Stat stat = target.CharacterItem.GetStat(statName);
            if (stat != null)
            {
                stat.BaseValue *= multiplier;
                yield return new WaitForSeconds(duration);
                if (target != null && target.CharacterItem != null) stat.BaseValue /= multiplier;
            }
        }

        private void ChangeModelColor(CharacterMainControl boss, Color color, bool useEmission, float emissionIntensity)
        {
            var renderers = boss.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
            {
                foreach (var mat in r.materials)
                {
                    if (mat.HasProperty("_Color")) mat.color = color;
                    else if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                    if (useEmission) {
                        mat.EnableKeyword("_EMISSION");
                        if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", color * emissionIntensity);
                    }
                }
            }
        }

        private void AddPointLight(CharacterMainControl boss, Color color, float range, float intensity)
        {
            GameObject lightObj = new GameObject("BossRageLight");
            lightObj.transform.SetParent(boss.transform, false);
            lightObj.transform.localPosition = Vector3.up * 2.0f; 
            Light light = lightObj.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.Soft; 
        }
    }
}