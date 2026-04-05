# Migration Effort Estimation Guide

## Purpose

Estimate the effort required for a library migration BEFORE committing to it. Prevents under-estimation (the #1 cause of stalled migrations).

---

## Step 1: Complexity Scoring

Score each module/unit in your Feature Inventory Matrix using this scale:

| Size | Criteria | Effort Estimate |
|:-----|:---------|:---------------|
| **XS** | Direct 1:1 API mapping, no behavioral differences | ~15 min per unit |
| **S** | Minor adaptation needed (parameter reorder, type cast) | ~30 min per unit |
| **M** | Partial mapping, needs wrapper/adapter function | ~1-2 hours per unit |
| **L** | No direct equivalent, custom implementation required | ~2-4 hours per unit |
| **XL** | Fundamentally different paradigm (e.g., sync→async, callback→stream) | ~4-8 hours per unit |

## Step 2: Count and Multiply

```
Total Effort = Σ (units × size_estimate) × Buffer Multiplier

Buffer Multiplier:
  - First migration of this type: × 2.5
  - Have done similar before:    × 1.5
  - Expert in both platforms:    × 1.2
```

## Step 3: Add Non-Coding Work

Coding is only ~40% of the total effort. Add:

| Activity | % of Coding Time |
|:---------|:----------------|
| Source analysis & inventory (Phase 0-1) | +25% |
| Golden Master capture (Phase 1.5) | +10% |
| API research & mapping (Phase 2) | +15% |
| Testing & verification (Phase 3-4) | +30% |
| Optimization & cleanup (Phase 5-6) | +20% |

## Step 4: Three-Point Estimate

For stakeholder communication, provide:

| Scenario | Calculation |
|:---------|:-----------|
| **Best Case** | Sum of XS/S only, expert multiplier |
| **Most Likely** | Full sum, appropriate multiplier, +25% buffer |
| **Worst Case** | Full sum × 2, discovery of additional scope |

**Formula**: `Expected = (Best + 4×MostLikely + Worst) / 6`

---

## Pilot Migration

⚠️ **Always** do a pilot migration of ONE small module before estimating the full project.

The pilot:
1. Validates your complexity assumptions
2. Reveals hidden dependencies and gotchas
3. Establishes your actual velocity (units/hour)
4. Updates your Buffer Multiplier with real data

## Red Flags That Increase Estimates

- [ ] Source code has no tests → add +50% for Golden Master creation
- [ ] Source code is undocumented → add +30% for discovery
- [ ] Target platform is new to the team → add +40% for learning curve
- [ ] Circular dependencies in source → add +20% for refactoring
- [ ] Source has known bugs that must be preserved → add +15% for careful handling
- [ ] Migration must maintain backward compatibility → add +25% for adapter layers
