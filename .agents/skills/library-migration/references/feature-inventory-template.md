# Feature Inventory Matrix (FIM)

## Migration Project
- **Source**: [Library/Framework Name + Version]
- **Target**: [Library/Framework Name + Version]
- **Date**: [YYYY-MM-DD]
- **Total Items**: [count]

---

## Inventory Legend

| Category | Prefix | Description |
|:---------|:-------|:------------|
| Feature | F | What the code does — functions, transformations, outputs |
| Rule | R | Business logic constraints, validations, limits |
| Behavior | B | Side effects, event handling, lifecycle hooks |
| Error | E | Error handling, reporting, recovery paths |
| Default | D | Default values, fallback behaviors, implicit configs |
| Config | C | Settings, options, parameters that modify behavior |

| Status | Symbol | Meaning |
|:-------|:-------|:--------|
| Pending | ⬜ | Not yet started |
| In Progress | 🔄 | Currently being converted |
| Done | ✅ | Converted and verified |
| Blocked | 🚫 | Cannot convert — needs resolution |
| N/A | ➖ | Not applicable to target platform |

| Priority | Level | Meaning |
|:---------|:------|:--------|
| P0 | Critical | Core functionality — must have |
| P1 | High | Important features — should have |
| P2 | Medium | Nice to have — can defer |
| P3 | Low | Edge case — document but deprioritize |

---

## Features (F)

| ID | Feature | Source Location | Input Example | Expected Output | Edge Cases | Priority | Status | Target Location | Tests |
|----|---------|-----------------|---------------|-----------------|------------|----------|--------|-----------------|-------|
| F001 | | | | | | P0 | ⬜ | | |
| F002 | | | | | | P0 | ⬜ | | |
| F003 | | | | | | P0 | ⬜ | | |

---

## Rules (R)

| ID | Rule | Source Location | Constraint | Violation Behavior | Edge Cases | Priority | Status | Target Location | Tests |
|----|------|-----------------|------------|-------------------|------------|----------|--------|-----------------|-------|
| R001 | | | | | | P0 | ⬜ | | |
| R002 | | | | | | P0 | ⬜ | | |

---

## Behaviors (B)

| ID | Behavior | Source Location | Trigger | Effect | Side Effects | Priority | Status | Target Location | Tests |
|----|----------|-----------------|---------|--------|-------------|----------|--------|-----------------|-------|
| B001 | | | | | | P0 | ⬜ | | |
| B002 | | | | | | P0 | ⬜ | | |

---

## Error Handling (E)

| ID | Error Case | Source Location | Error Type | Message | Recovery Action | Priority | Status | Target Location | Tests |
|----|-----------|-----------------|------------|---------|----------------|----------|--------|-----------------|-------|
| E001 | | | | | | P0 | ⬜ | | |
| E002 | | | | | | P0 | ⬜ | | |

---

## Defaults (D)

| ID | Default | Source Location | Value | When Applied | Impact If Changed | Priority | Status | Target Location | Tests |
|----|---------|-----------------|-------|-------------|-------------------|----------|--------|-----------------|-------|
| D001 | | | | | | P1 | ⬜ | | |
| D002 | | | | | | P1 | ⬜ | | |

---

## Configuration (C)

| ID | Config | Source Location | Type | Default | Valid Range | Priority | Status | Target Location | Tests |
|----|--------|-----------------|------|---------|-------------|----------|--------|-----------------|-------|
| C001 | | | | | | P1 | ⬜ | | |
| C002 | | | | | | P1 | ⬜ | | |

---

## Inventory Completeness Checklist

Before proceeding to Phase 2 (API Mapping), verify:

- [ ] All public methods/functions documented
- [ ] All private/internal helper functions documented  
- [ ] All event handlers and callbacks documented
- [ ] All error handling paths documented
- [ ] All default values and fallbacks documented
- [ ] All input validation rules documented
- [ ] All output formatting rules documented
- [ ] All state management patterns documented
- [ ] All configuration options documented
- [ ] All edge cases documented (null, empty, overflow, unicode, concurrent)
- [ ] Each item has at least one concrete input/output example
- [ ] Each item has identified edge cases
- [ ] Priority assigned to every item

## Summary

| Category | Total | P0 | P1 | P2 | P3 |
|:---------|:------|:---|:---|:---|:---|
| Features (F) | | | | | |
| Rules (R) | | | | | |
| Behaviors (B) | | | | | |
| Errors (E) | | | | | |
| Defaults (D) | | | | | |
| Config (C) | | | | | |
| **TOTAL** | | | | | |
