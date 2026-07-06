# Raidlands Bot Cover, Barricade, Healing, and Push Mechanics

## Purpose

This document captures the intended combat-survival behavior for the Raidlands roaming bots, especially the mechanics around natural cover, barricades, medical supplies, shooting back, and deciding when to hold or push.

The central design idea is that bots should feel player-like. They should not simply stand in the open, beam through walls, or chase hidden players with perfect knowledge. They should perceive danger, protect their health, use cover intelligently, heal according to the same combat logic available to players, and only push when they have earned enough confidence.

---

## Core Interpretation

The bot's survival loop should be:

```text
Take meaningful damage
→ check nearby natural cover / closest existing barricade
→ if protection is close enough, move to it
→ if protection is too far, place a barricade immediately
→ heal according to current health state
→ hold cover/barricade only until active healing is finished
→ peek and exchange fire
→ at long range, do not push from barricade until confidence condition is met
```

The key medical distinction is:

```text
Berries and medkits do not stop combat.
Syringes do stop combat.
```

If players can shoot while eating berries or using medkits, bots should be able to do the same. If syringe use prevents a player from shooting, syringe use should prevent the bot from shooting as well.

---

## Protection Distance Rules

When the bot needs protection, it should evaluate:

1. Natural cover.
2. Closest existing barricade.
3. Whether it must place a new barricade.

The valid protection distance should be tuned between **3m and 8m**, based primarily on bot skill.

| Bot Skill | Natural Cover / Existing Barricade Must Be Within | Intended Behavior |
|---|---:|---|
| Low skill | ~8m | Will try to run farther to cover, even when risky. More likely to misstep. |
| Medium skill | ~5m | Balanced protection behavior. Uses cover or barricade reasonably. |
| High skill | ~3m | Protects health fiercely. If protection is not immediately available, places a barricade instead of risking open movement. |

### Important Priority

For high-skill bots, the behavior should feel especially defensive and survival-focused:

```text
High-skill bot takes meaningful damage
→ if immediate protection is not very close
→ place barricade quickly
→ use natural cover fiercely when it is already available or tactically stronger
```

In other words, high-skill bots should protect their health aggressively with barricades and natural cover. They should not casually cross open ground after taking damage.

---

## Damage Trigger Rules

| Trigger / Situation | Bot Decision | Bot Action | Shooting Allowed? | Notes |
|---|---|---|---:|---|
| Bot loses more than **15% health** during combat | “I need protection now.” | Check for nearby natural cover or closest existing barricade. If neither is within the skill-based protection distance, place a barricade immediately. | Yes, unless using syringe | This is the main panic-protection trigger. |
| Natural cover or existing barricade is close enough | “Use existing protection.” | Move to nearest valid protection. | Yes | Close enough means 3m–8m depending on skill. |
| Natural cover / closest barricade is too far | “I cannot safely cross open ground.” | Place barricade between bot and threat. | Yes | Especially important after losing >15% health or dropping below 60%. |
| Bot is below 60% while exposed | “I need to break pressure before syringe.” | Move to close protection if available; otherwise place barricade first. | No while syringing | Bot should not stand in the open and syringe. |
| Bot has finished healing | “Re-enter the fight.” | Peek from cover/barricade and exchange fire. | Yes | Prevents the bot from hiding forever after healing. |

---

## Barricade Placement Logic

```text
OnDamageTaken:
  healthLostThisFight += damagePercent

  if healthLostThisFight > 15%:
      nearestProtection = nearest natural cover OR closest existing barricade
      protectionDistance = skillBasedProtectionDistance

      if nearestProtection exists AND distanceTo(nearestProtection) <= protectionDistance:
          move to nearestProtection
      else:
          place barricade between bot and threat
          move behind barricade

      start barricade cooldown / reset damage trigger window
```

### Barricade Placement Requirements

A barricade should be placed when:

```text
bot has taken >15% health damage
AND no natural cover or existing barricade is within the skill-based protection distance
AND bot has line-of-threat information or last-known threat direction
AND barricade placement is valid
AND barricade cooldown is ready
```

A barricade should not be placed when:

```text
valid protection is already close enough
OR placement is blocked / invalid
OR bot is in a restricted zone where barricades should not be used
OR barricade cooldown is active
OR the bot already has effective protection and does not need a new one
```

---

## Healing Rules

| Health State | Preferred Healing Behavior | Can Shoot? | Notes |
|---|---|---:|---|
| 100% | Do not heal. | Yes | No wasteful healing. |
| 60%–99% | Alternate berries and medkits. | Yes | Bot may continue fighting while using these. |
| Below 60% | Use syringe. | **No** | Syringe blocks shooting. |
| Below 60% and exposed | Get to cover or place barricade first, then use syringe. | **No during syringe** | Protection before syringe. |
| After syringe finishes but still below full health | Resume berry/medkit alternating. | Yes | Continue topping off while fighting. |

---

## Berry / Medkit Alternation

If the bot is below full health but not in syringe territory, it should alternate berries and medkits instead of spamming only one item.

```text
if health >= 60% AND health < 100%:
    if lastNonSyringeHealWasBerry:
        use medkit if available
        otherwise use berry if available
    else:
        use berry if available
        otherwise use medkit if available

    shooting remains allowed during berry or medkit use
```

Expected sequence:

```text
Berry → Medkit → Berry → Medkit → Berry → Medkit
```

Inventory availability can modify the sequence, but the bot should try to alternate whenever both are available.

---

## Syringe Rules

A syringe is the emergency heal.

```text
if health < 60%:
    if bot is exposed:
        if natural cover or existing barricade is within skill-based protection distance:
            move to that protection
        else:
            place barricade between bot and threat
            move behind barricade

    use syringe
    block shooting while syringe is active
```

### Important Syringe Behavior

The bot should not:

```text
stand in the open and syringe
shoot while using syringe
peek while the syringe action is active
push while the syringe action is active
```

The bot should:

```text
break pressure first
use cover or barricade
finish the syringe
then peek and exchange again
```

---

## Holding Cover While Healing

The bot should not hold cover forever. It should hold cover only long enough to finish its active healing action.

| Active Healing Action | Bot Should Hold Cover? | Shooting Allowed? | After Healing Finishes |
|---|---:|---:|---|
| Berry | Yes, but may still fight if LOS/peek is available. | Yes | Peek and exchange. |
| Medkit | Yes, but may still fight if LOS/peek is available. | Yes | Peek and exchange. |
| Syringe | Yes. | **No** | Re-engage after syringe completes. |

### Cover Hold Flow

```text
Bot reaches cover or barricade
→ start healing action
→ if berry or medkit: bot may still shoot when safely peeking
→ if syringe: bot does not shoot
→ healing action completes
→ bot peeks
→ bot exchanges fire
→ bot re-evaluates push, hold, retreat, or heal again
```

---

## Long-Range Barricade Anchor Mode

If the bot has placed a barricade and the player is far away, the bot should not immediately abandon the barricade and sprint forward.

Instead, it should enter **Barricade Anchor Mode**.

### Entry Condition

```text
bot has placed a barricade
AND distanceToPlayer > skillOrWeaponBasedLongRangeThreshold
```

The intended long-range threshold should be somewhere between **40m and 70m**.

| Bot Skill | Suggested Long-Range Threshold | Required Confidence Before Push |
|---|---:|---:|
| Low skill | 40m+ | 2 hitmarkers, or may push too early sometimes. |
| Medium skill | 55m+ | 3–4 hitmarkers. |
| High skill | 70m+ | 4–5 hitmarkers, confirmed kill, or cautious timeout investigation. |

---

## Barricade Anchor Movement Rules

While anchoring behind a barricade:

| Movement Type | Allowed? | Notes |
|---|---:|---|
| Move closer to the barricade | Yes | Bot can tighten to cover. |
| Move side-to-side behind the barricade | Yes | Bot can strafe, peek, and adjust angle. |
| Move in front of the barricade | **No** | Bot should not expose itself before it is confident enough to push. |
| Peek from the side | Yes | Only when trying to exchange fire. |
| Push forward | Only after confidence condition | Hitmarkers, confirmed death, or no-action timer. |

### Barricade Anchor Flow

```text
Bot places barricade
→ player is far away
→ bot anchors behind barricade

While anchored:
  stay behind barricade
  move closer to barricade if needed
  strafe side-to-side behind barricade
  do not walk in front of barricade
  heal if needed
  peek and shoot only when LOS exists

Push only if:
  hitmarkers >= requiredHitmarkersBySkill
  OR player is confirmed dead
  OR no action from player for X seconds
```

---

## Push Confidence Conditions

The bot should push away from behind the barricade only when it has a reason to believe the fight has shifted in its favor.

| Confidence Trigger | Meaning | Push Behavior |
|---|---|---|
| Bot lands 2–5 hitmarkers | Player is likely damaged or pressured. | Push according to skill level and tactical state. |
| Player is seen dying | Player is confirmed dead, including from another player or bot. | Advance or resume roaming/looting behavior. |
| No player action for X seconds | Player may have bled out, fled, repositioned, or gone silent. | Cautiously push to check. |

### Suggested Hitmarker Requirements

| Bot Skill | Hitmarkers Needed Before Push |
|---|---:|
| Low skill | 2 |
| Medium skill | 3–4 |
| High skill | 4–5 |

### Suggested No-Action Push Timer

| Bot Skill | No-Action Timer |
|---|---:|
| Low skill | 8–12 seconds |
| Medium skill | 12–18 seconds |
| High skill | 18–25 seconds |

High-skill bots should be more patient and health-protective. Low-skill bots can become impatient, misjudge the situation, and push too early.

---

## Shooting Rules While Using Cover or Barricades

The bot should only shoot when it has legitimate LOS.

```text
if hasLineOfSightToPlayer:
    if not using syringe:
        bot may shoot
else:
    bot must not shoot
```

### Important Shooting Constraints

The bot may shoot while:

```text
using berries
using medkits
peeking from cover
peeking from barricade
strafing behind barricade with valid LOS
```

The bot may not shoot while:

```text
using syringe
fully tucked with no LOS
player is behind a wall with no LOS
bot only has sound or last-known position
```

---

## Skill-Based Personality Matrix

| Mechanic | Low Skill Bot | Medium Skill Bot | High Skill Bot |
|---|---|---|---|
| Health protection | Reacts late or inconsistently. | Reacts after clear damage. | Reacts quickly after >15% damage. |
| Protection distance | Will run up to ~8m through danger. | Uses ~5m as a balanced threshold. | Demands ~3m immediate protection or barricades. |
| Barricade usage | May forget, delay, or place poorly. | Uses barricades when exposed. | Uses barricades fiercely to preserve health. |
| Natural cover usage | May choose bad cover or wrong side. | Mostly correct. | Uses natural cover tightly and fiercely. |
| Healing | May heal late or at a bad time. | Uses healing logically. | Preserves health aggressively. |
| Berry/medkit use | May fail to alternate perfectly. | Usually alternates. | Alternates efficiently. |
| Syringe use | May syringe too exposed sometimes. | Usually seeks protection first. | Almost always covers or barricades before syringing. |
| Barricade movement | May step out accidentally. | Mostly stays behind barricade. | Does not move in front unless intentionally pushing. |
| Long-range push | May push after 2 hits or impatience. | Pushes after 3–4 hits or timeout. | Waits for 4–5 hits, confirmed death, or safe timeout. |
| Peeking | Longer exposure. | Moderate exposure. | Short controlled peeks. |
| Mistakes | Intentional bad decisions allowed. | Rare mistakes. | Very few mistakes. |

---

## Tactical State Machine

```text
CombatEngaged
  ├─ health lost > 15%
  │    ├─ protection within skill distance
  │    │    → MoveToCoverOrExistingBarricade
  │    │    → HealIfNeeded
  │    │    → PeekAndExchange
  │    │
  │    └─ protection too far
  │         → PlaceBarricade
  │         → MoveBehindBarricade
  │         → HealIfNeeded
  │         → BarricadeAnchorOrPeek
  │
  ├─ health < 60%
  │    ├─ exposed
  │    │    ├─ protection within skill distance
  │    │    │    → MoveToProtection
  │    │    │    → UseSyringe
  │    │    │
  │    │    └─ protection too far
  │    │         → PlaceBarricade
  │    │         → MoveBehindBarricade
  │    │         → UseSyringe
  │    │
  │    └─ already protected
  │         → UseSyringe
  │
  ├─ health 60%-99%
  │    → AlternateBerryMedkit
  │    → ContinueShootingIfLOS
  │
  └─ barricade placed + long distance
       → BarricadeAnchorMode
          ├─ stay behind barricade
          ├─ move closer to barricade if needed
          ├─ side-step only behind barricade
          ├─ shoot/heal from cover
          └─ push only after confidence condition
```

---

## Full Combat Behavior Flow

```text
Roam / Patrol
  ├─ hears combat but has no LOS
  │    → InvestigateSound
  │    → scan for player
  │    → do not shoot unless LOS is gained
  │
  ├─ sees player
  │    → AcquireTarget
  │    → exchange fire if LOS exists
  │
  ├─ takes damage
  │    → evaluate health loss
  │    → if lost >15% health, seek protection or barricade
  │    → if below 60%, protect first, then syringe
  │    → if 60%-99%, berry/medkit alternate while fighting
  │
  ├─ loses LOS
  │    → stop shooting
  │    → search last-known position
  │    → reacquire only if LOS returns
  │
  ├─ behind cover / barricade
  │    → heal if needed
  │    → peek and exchange
  │    → decide hold, push, flank, or retreat
  │
  └─ long-range barricade fight
       → anchor behind barricade
       → strafe behind barricade only
       → land required hitmarkers or wait out no-action timer
       → cautiously push when confidence condition is met
```

---

## Codex-Ready Rule Set

```text
1. If bot loses more than 15% health in combat, it must immediately seek protection.

2. Protection means:
   - natural cover, or
   - existing barricade, or
   - newly placed barricade.

3. If natural cover or the closest existing barricade is within the skill-based protection distance,
   the bot should move to it.

4. If protection is farther than the skill-based distance, the bot should place a barricade
   between itself and the threat.

5. Skill-based protection distance:
   - low skill: 8m
   - medium skill: 5m
   - high skill: 3m

6. If bot is below full health but at or above 60%, it should alternate berry and medkit usage.

7. Berry and medkit usage must not block shooting.

8. If bot drops below 60%, it should use a syringe.

9. Syringe usage must block shooting.

10. If bot is exposed and below 60%, it should first move to nearby protection or place a barricade,
    then use the syringe.

11. Bot should hold cover only until the active healing action is complete.

12. After healing finishes, bot should peek and exchange fire again.

13. If bot has placed a barricade and player distance is greater than the skill/weapon long-range
    threshold, bot should enter Barricade Anchor Mode.

14. While anchoring behind a barricade, bot may move closer to the barricade or side-to-side behind it,
    but should not move in front of it.

15. From long-range Barricade Anchor Mode, bot should push only after:
    - 2–5 hitmarkers depending on skill,
    - confirmed player death,
    - or no player action for X seconds.

16. High-skill bots should protect health fiercely with barricades and natural cover.

17. Lower-skill bots should have controlled imperfections:
    - delayed barricade,
    - worse cover selection,
    - overexposure,
    - pushing too early,
    - stepping out from barricade,
    - slower healing decisions,
    - imperfect berry/medkit alternation.

18. Bots must only shoot when they have valid LOS and are not using a syringe.

19. Bots may shoot while using berries or medkits if LOS exists.

20. Bots must stop shooting immediately when LOS breaks.
```

---

## Suggested Config Values

```toml
[CombatProtection]
DamageLossTriggerPercent = 15.0
LowSkillProtectionDistance = 8.0
MediumSkillProtectionDistance = 5.0
HighSkillProtectionDistance = 3.0

[Healing]
SyringeHealthThresholdPercent = 60.0
AllowShootingWhileUsingBerries = true
AllowShootingWhileUsingMedkits = true
AllowShootingWhileUsingSyringe = false
AlternateBerryAndMedkit = true
HoldCoverUntilHealComplete = true

[BarricadeAnchor]
LowSkillLongRangeThreshold = 40.0
MediumSkillLongRangeThreshold = 55.0
HighSkillLongRangeThreshold = 70.0
LowSkillRequiredHitmarkers = 2
MediumSkillRequiredHitmarkersMin = 3
MediumSkillRequiredHitmarkersMax = 4
HighSkillRequiredHitmarkersMin = 4
HighSkillRequiredHitmarkersMax = 5
LowSkillNoActionPushSecondsMin = 8.0
LowSkillNoActionPushSecondsMax = 12.0
MediumSkillNoActionPushSecondsMin = 12.0
MediumSkillNoActionPushSecondsMax = 18.0
HighSkillNoActionPushSecondsMin = 18.0
HighSkillNoActionPushSecondsMax = 25.0
PreventMovingInFrontOfBarricadeWhileAnchored = true
```

---

## Regression Checklist

| Mechanic | Pass Condition |
|---|---|
| >15% damage trigger | Bot reacts by seeking protection or placing barricade. |
| Protection distance | Bot uses natural cover or closest barricade only if within skill-based 3m–8m distance. |
| Barricade placement | Bot places barricade when exposed and protection is too far. |
| Berry/medkit healing | Bot alternates berry and medkit while below full health and at/above 60%. |
| Berry/medkit shooting | Bot can shoot while using berries or medkits. |
| Syringe threshold | Bot uses syringe below 60% health. |
| Syringe shooting block | Bot cannot shoot while using syringe. |
| Exposed syringe behavior | Bot seeks cover or places barricade before syringing in the open. |
| Cover hold | Bot holds cover until active heal completes, then peeks and exchanges. |
| Barricade anchor | At long range after placing barricade, bot stays behind barricade. |
| Barricade movement | Bot moves closer or side-to-side behind barricade, but not in front. |
| Push confidence | Bot pushes after required hitmarkers, confirmed death, or no-action timer. |
| Skill variation | High-skill bots protect health fiercely; low-skill bots make controlled mistakes. |
| LOS discipline | Bot only shoots with LOS and stops shooting when LOS breaks. |
