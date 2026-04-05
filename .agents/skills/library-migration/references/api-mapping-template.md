# API Mapping Matrix

## Migration Project
- **Source**: [Library/Framework Name + Version]
- **Target**: [Library/Framework Name + Version]
- **Date**: [YYYY-MM-DD]

---

## Mapping Legend

| Match Level | Symbol | Meaning |
|:------------|:-------|:--------|
| Full Match | ✅ | Direct equivalent, same behavior |
| Partial Match | ⚠️ | Similar but with differences — needs adaptation |
| No Match | ❌ | No equivalent — custom implementation required |
| N/A | ➖ | Not needed in target platform |

---

## API Mappings

### Category: [e.g., Data Access, Formatting, Events]

| # | Source API | Target API | Match | Differences | Adaptation Needed |
|---|:----------|:-----------|:-----:|:------------|:------------------|
| 1 | | | | | |
| 2 | | | | | |

### Category: [e.g., Error Handling]

| # | Source API | Target API | Match | Differences | Adaptation Needed |
|---|:----------|:-----------|:-----:|:------------|:------------------|
| 1 | | | | | |

---

## Verification Checklist for Each Mapping

For EVERY mapping entry, verify:

- [ ] **Signature**: Parameters match in type and order?
- [ ] **Return type**: Return type is equivalent?
- [ ] **Side effects**: Same side effects?
- [ ] **Error behavior**: Same errors thrown?
- [ ] **Null handling**: Same behavior with null/undefined/empty?
- [ ] **Threading/async**: Compatible concurrency model?
- [ ] **Performance**: Acceptable performance characteristics?

---

## Adaptation Functions Required

Custom wrapper functions needed where no direct mapping exists:

| # | Function Name | Purpose | Wraps Source API | Uses Target API | Complexity |
|---|:-------------|:--------|:-----------------|:---------------|:-----------|
| 1 | | | | | |

---

## Platform Behavioral Differences

Document known differences between source and target platforms:

| # | Area | Source Behavior | Target Behavior | Impact | Resolution |
|---|:-----|:---------------|:----------------|:-------|:-----------|
| 1 | Null semantics | `undefined` returned | `null` returned | Low — cosmetic | Accept difference |
| 2 | Array indexing | 0-based | 1-based | High — logic errors | Index conversion helper |
| 3 | | | | | |

---

## Mapping Summary

| Match Level | Count | Percentage |
|:------------|:------|:-----------|
| ✅ Full Match | | |
| ⚠️ Partial Match | | |
| ❌ No Match | | |
| ➖ N/A | | |
| **Total** | | |
