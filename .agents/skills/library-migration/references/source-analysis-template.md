# Source Analysis Document

## Migration Project
- **Source**: [Library/Framework Name + Version]
- **Target**: [Library/Framework Name + Version]
- **Date**: [YYYY-MM-DD]
- **Analyst**: [Name]

---

## 1. Source Codebase Overview

### 1.1 File Structure
```
source/
├── [list all relevant files]
├── [with their purposes]
└── [and line counts]
```

### 1.2 Entry Points
| Entry Point | Type | File:Line | Description |
|:------------|:-----|:----------|:------------|
| | Public API | | |
| | Event Handler | | |
| | Callback | | |
| | Export | | |

### 1.3 Dependencies
| Dependency | Version | Usage | Target Equivalent |
|:-----------|:--------|:------|:------------------|
| | | | |

---

## 2. Data Flow Analysis

### 2.1 Input Sources
| Input | Type | Format | Validation Rules |
|:------|:-----|:-------|:-----------------|
| | | | |

### 2.2 Output Destinations
| Output | Type | Format | Consumers |
|:-------|:-----|:-------|:----------|
| | | | |

### 2.3 Data Transformation Pipeline
```
[Input] → [Step 1: ...] → [Step 2: ...] → [Output]
```

---

## 3. Implicit Behaviors

### 3.1 Error Handling Patterns
| Pattern | Location | Behavior | Recovery |
|:--------|:---------|:---------|:---------|
| | | | |

### 3.2 Default Values
| Variable/Config | Default | Where Set | Impact |
|:----------------|:--------|:----------|:-------|
| | | | |

### 3.3 Type Coercions / Implicit Conversions
| Expression | Input Type | Coerced To | Behavior |
|:-----------|:-----------|:-----------|:---------|
| | | | |

### 3.4 Side Effects
| Function | Side Effect | Observable By |
|:---------|:------------|:-------------|
| | | |

---

## 4. Configuration & Environment

### 4.1 Environment Variables
| Variable | Required | Default | Description |
|:---------|:---------|:--------|:------------|
| | | | |

### 4.2 Config Files
| File | Format | Keys | Description |
|:-----|:-------|:-----|:------------|
| | | | |

### 4.3 Feature Flags / Toggles
| Flag | Default | Effect When Enabled | Effect When Disabled |
|:-----|:--------|:--------------------|:--------------------|
| | | | |

---

## 5. Complexity Hotspots

### 5.1 Complex Logic
| Location | Complexity | Description | Risk Level |
|:---------|:-----------|:------------|:-----------|
| | High/Med/Low | | 🔴/🟡/🟢 |

### 5.2 Platform-Specific Code
| Location | Platform Dependency | Migration Impact |
|:---------|:-------------------|:-----------------|
| | | |

---

## 6. Test Coverage Assessment

### 6.1 Existing Tests
| Test File | Coverage Area | Test Count | Quality |
|:----------|:-------------|:-----------|:--------|
| | | | Good/Fair/Poor |

### 6.2 Untested Areas (Risk Zones)
| Area | Why Untested | Risk If Missed |
|:-----|:-------------|:---------------|
| | | |

---

## 7. Migration Risk Summary

| Risk | Likelihood | Impact | Mitigation |
|:-----|:-----------|:-------|:-----------|
| | High/Med/Low | High/Med/Low | |

## 8. Estimated Effort

| Phase | Estimated Units | Complexity | Time Estimate |
|:------|:---------------|:-----------|:-------------|
| Phase 0: Discovery | — | — | [done] |
| Phase 1: Inventory | | | |
| Phase 2: Mapping | | | |
| Phase 3: Conversion | | | |
| Phase 4: Parity Gate | | | |
| Phase 5: Optimization | | | |
